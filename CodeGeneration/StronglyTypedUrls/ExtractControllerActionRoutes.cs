
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Sfcd.Utility;
using Sfcd.Utility.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Diagnostics.Telemetry;
using Microsoft.CodeAnalysis.Differencing;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Recommendations;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Tags;
using Microsoft.CodeAnalysis.Text;

#region Records

public record RouteAttrib( String attribName, String? routeTemplate )
{
	public String HttpMethod
	{
		get
		{
			if     ( this.attribName == "Route"       || this.attribName == "RouteAttribute"       ) return "ALL";
			else if( this.attribName == "AcceptVerbs" || this.attribName == "AcceptVerbsAttribute" ) return "TODO"; // HACK

			return this.attribName.TrimPrefix( "Http" ).ToUpperInvariant(); // HACK
		}
	}
}

public record ActionParam( String csType, String name, String csName, Boolean isQueryStringParam, Boolean isOptional /* isOptional == has default value or is nullable, and doesn't have [BindRequired] */ );

public record ControllerInfo( FileInfo file, String controllerClassName, String? controllerRouteTemplate );

public record ActionRoute( ControllerInfo controller, RouteAttrib actionRoute, String actionName, String routeTemplate, IReadOnlyList<ActionParam> parameters, MethodDeclarationSyntax methodSyntax )
{
	// hmm, params too?

	public String HttpMethod => this.actionRoute.HttpMethod;

	public IEnumerable<String> CrefParamTypes => this.methodSyntax.ParameterList.Parameters.Select( p => p.Type!.ToString() );
}

#endregion

public static class ControllerActionRouteExtractor
{
#region Input and processing

public static IReadOnlyList<ActionRoute> ExtractActionRoutes( params DirectoryInfo[] dirs )
{
	List<ActionRoute> list = new List<ActionRoute>();

	List<FileInfo> sourceCSFiles = new List<FileInfo>();
	sourceCSFiles.AddRange( dirs.SelectMany( d => d.GetFiles( "*.cs", SearchOption.AllDirectories ) ) );

	foreach( FileInfo csFile in sourceCSFiles )
	{
		foreach( ActionRoute ar in ExtractActionRoutes( csFile ) )
		{
			list.Add( ar );
		}
	}

	return list;
}

static IReadOnlyList<ActionRoute> ExtractActionRoutes( FileInfo csFile )
{
	String cs = File.ReadAllText( csFile.FullName );

	SyntaxTree tree = CSharpSyntaxTree.ParseText( cs );

	if( !tree.HasCompilationUnitRoot )
	{
		return Array.Empty<ActionRoute>();
	}

	// https://stackoverflow.com/questions/31222085/using-roslyn-to-parse-classes-functions-and-properties
	CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

	List<ClassDeclarationSyntax> classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();

	var list = classes.SelectMany( cls => ExtractActionRoutesInner( csFile, cls ) ).ToList();

	return list;
}

static IEnumerable<ActionRoute> ExtractActionRoutesInner( FileInfo csFile, ClassDeclarationSyntax cls )
{
	IReadOnlyList<String> controllerRouteAttribs = cls.AttributeLists
//	IReadOnlyList<( AttributeSyntax attrib, String routeTemplate )> controllerRouteAttribs = cls.AttributeLists
		.SelectMany( al => al.Attributes )
		.Select( attrib => ( attrib, ok: IsRouteAttribute( attrib, out String? routeTemplate ), routeTemplate ) ) // `HttpMethodAttribute` (e.g. `HttpGet` etc) cannot be applied to classes, only [Route] can, btw.
		.Where( t => t.ok )
//		.Select( t => ( t.attrib, t.routeTemplate! ) )
		.Select( t => t.routeTemplate! )
		.ToList();

	List<MethodDeclarationSyntax> publicInstanceMethods = cls
		.DescendantNodes()
		.OfType<MethodDeclarationSyntax>() // https://stackoverflow.com/questions/51970239/getting-public-functions-using-roslyn?rq=1
		.Where( md => IsPublicNonStatic( md ) )
		.ToList();

	//

	String className = cls.Identifier.ToString();

	IReadOnlyList<String> classRoutePrefixes;
	if( controllerRouteAttribs.Count > 0 )
	{
		classRoutePrefixes = controllerRouteAttribs;
	}
	else
	{
		classRoutePrefixes = new String[] { "" };
	}

	foreach( MethodDeclarationSyntax publicInstanceMethod in publicInstanceMethods )
	{
		// TODO: Public instance action methods on controllers with route attributes but no action route attributes?

		IReadOnlyList<RouteAttrib> routeAttributes = GetAllRouteAttribs( publicInstanceMethod ).ToList();
		if( routeAttributes.Count > 0 )
		{
			String methodName = publicInstanceMethod.Identifier.ToString();

			foreach( String classRoutePrefix in classRoutePrefixes )
			{
				ControllerInfo controllerInfo = new ControllerInfo( csFile, className, controllerRouteTemplate: classRoutePrefix );

				foreach( RouteAttrib routeAttrib in routeAttributes )
				{
					String fullTemplate = StringJoiner.JoinPresent( sep: '/', classRoutePrefix, routeAttrib.routeTemplate );

					IReadOnlyList<ActionParam> parameters = GetRouteParametersFromControllerMethod( publicInstanceMethod, fullTemplate );

					yield return new ActionRoute(
						controller   : controllerInfo,
						actionRoute  : routeAttrib,
						actionName   : methodName,
						routeTemplate: fullTemplate,
						parameters   : parameters,
						methodSyntax : publicInstanceMethod
					);
				}
			}
		}
	}
}


static Boolean IsRouteAttribute( AttributeSyntax attrib, [NotNullWhen(true)] out RouteAttrib? routeAttrib )
{
	if( IsRouteAttribute( attrib, out String? routeTemplate ) )
	{
		routeAttrib = new RouteAttrib( attribName: attrib.Name.ToString(), routeTemplate );
		return true;
	}

	routeAttrib = null;
	return false;
}

static Boolean IsRouteAttribute( AttributeSyntax attrib, [NotNullWhen(true)] out String? routeTemplate )
{
	String attributeName = attrib.Name.ToString();

	if( _routeAttributeNames.Contains( attributeName ) )
	{
		routeTemplate = GetFirstStringLiteralDesc( attrib ) ?? String.Empty;
		return true;
	}

	routeTemplate = default;
	return false;
}

static IEnumerable<RouteAttrib> GetAllRouteAttribs( MethodDeclarationSyntax publicInstanceMethod )
{
	foreach( AttributeSyntax attrib in publicInstanceMethod.AttributeLists.SelectMany( ( AttributeListSyntax list ) => list.Attributes ) )
	{
		if( IsRouteAttribute( attrib, out RouteAttrib? parsed ) )
		{
			yield return parsed;
		}
	}
}

static IReadOnlyList<ActionParam> GetRouteParametersFromControllerMethod( MethodDeclarationSyntax method, String routeTemplate )
{
	List<ActionParam> list = new List<ActionParam>();

	foreach( ParameterSyntax p in method.ParameterList.Parameters )
	{
		String csName = p.Identifier.ToString(); // TODO: [FromRoute], [FromQuery]
		String csType = p.Type!.ToString();

		( ActionParamType paramType, String? nameOverride, Boolean bindRequired ) = GetActionParamType( p );
		if( paramType != ActionParamType.None )
		{
			String effectiveName = nameOverride ?? csName;

			Boolean isQueryStringParam = !routeTemplate.ContainsWord( effectiveName );

			Boolean isOptional;
			if( bindRequired )
			{
				isOptional = false;
			}
			else
			{
				isOptional = p.Default != null || p.Type is NullableTypeSyntax;
			}

			list.Add( new ActionParam( csType: csType, name: effectiveName, csName: csName, isQueryStringParam, isOptional: isOptional ) );
		}
	}

	return list;
}

static ( ActionParamType paramType, String? nameOverride, Boolean bindRequired ) GetActionParamType( ParameterSyntax p )
{
	List<AttributeSyntax> attribs = p.AttributeLists
		.SelectMany( al => al.Attributes )
//		.Where( a => _routeParameterAttributeNames.Contains( a.Name.ToString() ) )
		.ToList();

	ActionParamType paramType    = ActionParamType.Undefined;
	Boolean         bindRequired = attribs.Any( a => a.Name.ToString() == "BindRequired" );
	String?         nameOverride = null;

	foreach( AttributeSyntax attrib in attribs )
	{
		String attribName = attrib.Name.ToString();

		if( _actionParameterAttributeNames.TryGetValue( attribName, out ActionParamType type ) )
		{
			switch( type )
			{
			case ActionParamType.FromQuery:
			case ActionParamType.FromRoute:
				nameOverride = GetFirstStringLiteralDesc( attrib );
				paramType    = type;
				break;
			case ActionParamType.None:
				nameOverride = null;
				paramType    = type;
				break;

			case ActionParamType.Undefined:
			default:
				throw new InvalidOperationException( "This should never happen." );
			}
		}
	}

	return ( paramType, nameOverride, bindRequired );
}

public enum ActionParamType
{
	None,
	Undefined, // i.e. Route-or-Query (Route if it's in the route template, otherwise querystring)

	FromRoute,
	FromQuery
}

static String? GetFirstStringLiteralDesc( AttributeSyntax attrib )
{
	// [Route] uses a string ctor param.
	// [FromRoute] uses a named-parameter instead, grrr.

	if( attrib.DescendantNodes().OfType<LiteralExpressionSyntax>().FirstOrDefault() is LiteralExpressionSyntax litExp ) // it's lit 🔥
	{
		return litExp.Token.ToString().Trim( '"' ); // HACK: How to properly get a string literal as a "real" C# string?
	}

	return null;
}


#endregion

#region Output

public static void WriteOutput( FileInfo outputFilePath, String outputTemplateText, IReadOnlyList<ActionRoute> routes )
{
	String prefixContent;
	String suffixContent;
	{
		const String REPLACEMENT_TOKEN = "GENERATED_CODE_GOES_HERE";

		Int32 replacementTokenIdx = outputTemplateText.IndexOf( REPLACEMENT_TOKEN, StringComparison.Ordinal );
		if( replacementTokenIdx < 0 ) throw new FormatException( message: "Output template text does not contain the required placeholder \"" + REPLACEMENT_TOKEN + "\"." );

		prefixContent = outputTemplateText.Substring( startIndex: 0, length: replacementTokenIdx );
		suffixContent = outputTemplateText.Substring( startIndex: replacementTokenIdx + REPLACEMENT_TOKEN.Length );
	}

	StringBuilder sb = new StringBuilder();
	using( StringWriter w = new StringWriter( sb, CultureInfo2.InvariantCultureWithIso8601 ) )
	{
		w.Write( prefixContent );

		WriteOutputMembers( w, routes );

		w.WriteLine( suffixContent );
	}

	sb.AlignLines();
	String outputCS = sb.ToString();

	File.WriteAllText( path: outputFilePath.FullName, contents: outputCS );
}

static void WriteOutputMembers( StringWriter w, IReadOnlyList<ActionRoute> routes )
{
	foreach( ActionRoute r in routes.OrderBy( rt => rt.controller.controllerClassName ).ThenBy( rt => rt.actionName ) )
	{
		if( r.parameters.Count == 0 )
		{
			WriteOutputMemberNoParams( w, r );
		}
		else
		{
			WriteOutputMemberWithParams( w, r );
		}

		w.WriteLine();
	}
}

static void WriteOutputMemberNoParams( StringWriter w, ActionRoute r )
{
	String ctrl          = r.controller.controllerClassName;
	String ctrlShort     = ctrl.TrimSuffix( "Controller" );
	String extMethodName = ctrlShort + "_" + r.actionName + "_" + r.HttpMethod;

	WriteXmlDoc( w, r );
	w.Write( "\t\tpublic static String {0}( this IUrlHelper url ) => url.Action( action: nameof({1}.{2}), controller: {1}.Name );".FmtInv( extMethodName, ctrl, r.actionName ) );
	w.WriteLine();
	w.WriteLine();
}

static void WriteOutputMemberWithParams( StringWriter w, ActionRoute r )
{
	String ctrl             = r.controller.controllerClassName;
	String ctrlShort        = ctrl.TrimSuffix( "Controller" );
	String actionMethodName = r.actionName;
	String extMethodName    = ctrlShort + "_" + actionMethodName + "_" + r.HttpMethod;

	String paramsFlat       = r.parameters.Select( ToUrlExtensionsParam ).StringJoin( separator: ", " );

	WriteXmlDoc( w, r );
	w.Write( "\t\tpublic static String {0}( this IUrlHelper url, {1} )".FmtInv( extMethodName, paramsFlat ) );
	w.WriteLine();
	w.Write( "\t\t{" );
	w.WriteLine();

	w.Write( "\t\t\tRouteValueDictionary dict = new RouteValueDictionary();" );
	w.WriteLine();

	foreach( ActionParam p in r.parameters )
	{
		if( p.isQueryStringParam && p.csType == "String?" )
		{
			w.Write( "\t\t\tif( {1}¦.IsSet() ¦) dict.Add( \"{1}\"¦, {1} ¦);".FmtInv( p.name, p.csName ) );
		}
		else if( p.csType.EndsWith("?") || p.isOptional )
		{
			w.Write( "\t\t\tif( {1}¦ != null ¦) dict.Add( \"{1}\"¦, {1} ¦);".FmtInv( p.name, p.csName ) );
		}
		else
		{
			w.Write( "\t\t\tdict.Add( \"{1}\", {1} );".FmtInv( p.name, p.csName ) );
		}


		w.WriteLine();
	}

	w.WriteLine();

	w.Write( "\t\t\treturn url.Action( action: nameof({0}.{1}), controller: {0}.Name, values: dict );".FmtInv( ctrl, actionMethodName ) );
	w.WriteLine();

	w.WriteLine();
	w.Write( "\t\t}" );
	w.WriteLine();

	//

	static String ToUrlExtensionsParam( ActionParam p )
	{
		if( p.isOptional )
		{
			String nullableCSType = p.csType + ( p.csType.EndsWith("?") ? "" : "?" );
			return nullableCSType + " " + p.csName + " = default";
		}
		else
		{
			return p.csType + " " + p.csName;
		}
	}
}

static void WriteXmlDoc( StringWriter w, ActionRoute r )
{
	static string ToTypeDoc( String csType )
	{
		if( _simpleTypes.Contains( csType ) )
		{
			return csType.TrimEnd('?');
		}
		else
		{
			return "<see cref=\"{0}\"/>".FmtInv( csType.TrimEnd('?') );
		}
	}

	static string ToParamDoc( ActionParam p )
	{
		if( p.csName == p.name )
		{
			return "{0} {1}".FmtInv( ToTypeDoc( p.csType ), p.name );
		}
		else
		{
			return "{0} {1} ({2})".FmtInv( ToTypeDoc( p.csType ), p.name, p.csName );
		}
	}

	String paramsDocFlat = r.parameters.Select( ToParamDoc ).StringJoin( separator: ", ", listPrefix: " ", listSuffix: " " );

	String methodCrefTypes = r.parameters.Select( ToParamDoc ).StringJoin( sep: ',' );

	String controllerCls = r.controller.controllerClassName;

	String controllerActionCref = "<see cref=\"{0}.{1}({2})\"/>".FmtInv( controllerCls, r.actionName, String.Join( ",", r.CrefParamTypes ) );

	w.Write( "\t\t/// <summary><c>{0} {1}</c><br />Handled by <c>{2}</c>.</summary>".FmtInv( /*0:*/ r.HttpMethod, /*1:*/ r.routeTemplate, /*2:*/ controllerActionCref ) );//, /*3:*/ methodCrefTypes, /*4:*/ paramsDocFlat ) );
	w.WriteLine();
}

#endregion

#region Roslyn Predicates

static Boolean IsPublicNonStatic( MethodDeclarationSyntax method ) => IsPublicNonStatic( method.Modifiers );

static Boolean IsPublicNonStatic( SyntaxTokenList modifiers )
{
	// Methods are private non-static by default, therefore ensure `public` exists and `static` does not.
	Boolean isPublic = modifiers.Any( m => m.Kind() == SyntaxKind.PublicKeyword );
	Boolean isStatic = modifiers.Any( m => m.Kind() == SyntaxKind.StaticKeyword );

	return isPublic && !isStatic;
}

static Boolean HasRouteAttributes( SyntaxList<AttributeListSyntax> attributeLists ) => attributeLists.Any( list => HasRouteAttributes( list ) );

static Boolean HasRouteAttributes( AttributeListSyntax attributeList )
{
	return attributeList.Attributes.Any( attrib => IsRouteAttribute( attrib, out String? _ ) );
}

#endregion

#region HashSets

static HashSet2<String> CreateRouteAttributeNamesSet()
{
	String[] attributeNames = new String[]
	{
		// HttpMethodAttribute subclasses can only be applied to methods.
		// But [Route] can be applied to classes too.
		"HttpDelete",
		"HttpGet",
		"HttpHead",
		"HttpOptions",
		"HttpPatch",
		"HttpPost",
		"HttpPut",

		"Route", // implements IRouteTemplateProvider
		"AcceptVerbs"  // implements IRouteTemplateProvider
	};

	HashSet2<String> hs = new HashSet2<String>( StringComparer.OrdinalIgnoreCase );
	foreach( String attributeName in attributeNames )
	{
		_ = hs.Add( attributeName );
		_ = hs.Add( attributeName + "Attribute" );
	}

	return hs;
}

static Dictionary<String,ActionParamType> CreateActionParameterAttributeNamesDict()
{
	( String name, ActionParamType type )[] attributeNames = new ( String name, ActionParamType type )[]
	{
		( "FromBody"    , ActionParamType.None      ),
		( "FromForm"    , ActionParamType.None      ),
		( "FromHeader"  , ActionParamType.None      ),
		( "FromQuery"   , ActionParamType.FromQuery ),
		( "FromRoute"   , ActionParamType.FromRoute ),
		( "FromServices", ActionParamType.None      )
	};

	Dictionary<String,ActionParamType> dict = new Dictionary<String,ActionParamType>( StringComparer.OrdinalIgnoreCase );
	foreach( ( String name, ActionParamType type ) pair in attributeNames )
	{
		dict.Add( pair.name              , pair.type );
		dict.Add( pair.name + "Attribute", pair.type );
	}

	return dict;
}

static readonly HashSet2<String> _routeAttributeNames = CreateRouteAttributeNamesSet();

static readonly Dictionary<String,ActionParamType> _actionParameterAttributeNames = CreateActionParameterAttributeNamesDict();

static readonly HashSet2<String> _simpleTypes = new HashSet2<String>( new[] { "String?", "String", "Int32", "Int32?" }, StringComparer.OrdinalIgnoreCase );

#endregion

}
