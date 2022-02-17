// <#+ /* https://stackoverflow.com/questions/24957737/t4-tt-using-custom-classes-in-tt-files

// */

public class ViewInfo
{
	public ViewInfo( FileInfo cshtmlFile, String t4RelativePath/*, String projectRelativePath*/, String? modelDecl, IEnumerable<String> requiredViewDataTypes )
	{
		this.CshtmlFile          = cshtmlFile;
		this.T4RelativePath      = t4RelativePath;
//		this.ProjectRelativePath = projectRelativePath;
		this.ProjectRelativePath = "Views\\" + t4RelativePath;
		this.ModelTypeName       = modelDecl;
		this.IsPartial           = cshtmlFile.Name.StartsWith( '_' ) || cshtmlFile.Name.ContainsIns( "partial" );

		this.CSName              = t4RelativePath.Replace( "\\", "_" ).TrimSuffix( "cshtml" ).Replace( ".", "" ).Trim( '_' ).Replace( "__", "_" );

		if( this.IsPartial )
		{
			this.CSName = this.CSName.Replace( "partial", "", StringComparison.OrdinalIgnoreCase );
		}

		this.AspNetRelativePath = @"~/Views/" + this.T4RelativePath.Replace( '\\', '/' );

		//

		this.ViewDataTypeNames = requiredViewDataTypes
			.Select( typeName => ( typeName, paramName: CSharpNaming.ConvertPascalToCamelCase( typeName.TrimStart('I') ) ) )
			.OrderBy( t => t.typeName )
			.ToList();

		if( this.ViewDataTypeNames.Count > 0 )
		{
			this.MethodParamsForViewData = ", " + this.ViewDataTypeNames.Select( t => "{0} {1}".FmtInv( t.typeName, t.paramName ) ).StringJoin( ", " );
			this.MethodArgsForRender     = ", " + this.ViewDataTypeNames.Select( t => t.paramName ).StringJoin( ", " );
		}

	}

	public FileInfo CshtmlFile          { get; }
	public String   T4RelativePath      { get; } // Path relative to this T4 file.
	public String   ProjectRelativePath { get; } // Path relative to .csproj
	public String?  ModelTypeName       { get; }

	public List<( String typeName, String paramName )> ViewDataTypeNames { get; }

	public String   CSName              { get; }
	public String   AspNetRelativePath  { get; } // e.g. `~/Views/Account/Index.cshtml`

	public Boolean  IsPartial           { get; }

	public String MethodParamsForViewData { get; } = "";
	public String MethodArgsForRender     { get; } = "";
}

static readonly Regex _modelRegex = new Regex( @"^@model\s+(.+?)\r?$", RegexOptions.Compiled | RegexOptions.Multiline );

// https://stackoverflow.com/questions/17003799/what-are-regular-expression-balancing-groups
static readonly Regex _dataRegex = new Regex( @"^.+?\.RequireViewData\<(.+)\>\(\)", RegexOptions.Compiled | RegexOptions.Multiline );

static IEnumerable<ViewInfo> GetViews( DirectoryInfo viewsRoot )
{
	FileInfo[] allCshtml = viewsRoot.GetFiles( "*.cshtml", SearchOption.AllDirectories );

	foreach( FileInfo cshtmlFile in allCshtml )
	{
		// Exclude infrastructure:
		if( cshtmlFile.Name == "_ViewImports.cshtml" || cshtmlFile.Name == "_ViewStart.cshtml" ) continue;

		String cshtmlText = File.ReadAllText( cshtmlFile.FullName );

		String? modelDecl = null;

		Match modelTypeNameMatch = _modelRegex.Match( cshtmlText );
		if( modelTypeNameMatch.Success )
		{
			modelDecl = modelTypeNameMatch.Groups[1].Value.Trim();

			Int32 commentIdx = modelDecl.IndexOf( "//" );
			if( commentIdx > -1 ) modelDecl = modelDecl.Substring( startIndex: 0, length: commentIdx );
		}

		String relativePath = cshtmlFile.FullName.TrimPrefix( viewsRoot.FullName ).Trim( '/', '\\' );

		yield return new ViewInfo( cshtmlFile, relativePath, modelDecl, GetRequiredViewDataTypes( cshtmlText ) );
	}
}

static HashSet<String> GetRequiredViewDataTypes( String cshtmlText )
{
	MatchCollection viewDataMatches = _dataRegex.Matches( cshtmlText );

	HashSet<String> list = new HashSet<String>();

	foreach( Match m in viewDataMatches )
	{
		String lineBefore = m.Value;
		if( !lineBefore.Contains( "//" ) && !lineBefore.Contains( "/*" ) )
		{
			list.Add( m.Groups[1].Value );
		}
	}

	return list;
}

///////////////////////////

public class ContentFile
{
	public ContentFile( DirectoryInfo wwwrootDir, FileInfo file )
	{
		this.WwwrootDir = wwwrootDir ?? throw new ArgumentNullException(nameof(wwwrootDir));
		this.File       = file       ?? throw new ArgumentNullException(nameof(file));

		//

		String wwwrootRelativeFSPath = file.FullName.Substring( startIndex: wwwrootDir.FullName.Length );
		String wwwrootRelativeUrl    = wwwrootRelativeFSPath.Replace( '\\', '/' ).TrimStart( '/' );

		this.VirtualPath = "~/" + wwwrootRelativeUrl;

		this.CSName = BuildCSName( wwwrootRelativeUrl );
	}

	private static String BuildCSName( String wwwrootRelativeUrl )
	{
		StringBuilder sb = new StringBuilder();

		Boolean afterUnderscore = true;

		foreach( Char c in wwwrootRelativeUrl )
		{
			if( Char.IsLetterOrDigit( c ) )
			{
				_ = sb.Append( afterUnderscore ? Char.ToUpperInvariant( c ) : c );

				afterUnderscore = false;
			}
			else
			{
				switch( c )
				{
				// Verboten:
				case '/':
				case '\\':
				case '.':
				case '-':
					_ = sb.Append( '_' );
					afterUnderscore = true;
					break;

				// Pass-through:
				case '_':
					if( !afterUnderscore )
					{
						_ = sb.Append( c );
						afterUnderscore = true;
					}
					break;

				// Drop:
				case ' ': // Drop spaces.
					afterUnderscore = true;
					break;
				default:
					afterUnderscore = false;
					break;
				}
			}
		}

		String csName = sb.ToString();
		return csName;
	}

	public DirectoryInfo WwwrootDir  { get; }
	public FileInfo      File        { get; }

	public String        VirtualPath { get; }
	public String        CSName      { get; }
}

static readonly HashSet<String> _contentFileExts = new HashSet<String>( new[] { ".png", ".svg", /*".map",*/ ".js", ".ico", ".css" }, StringComparer.OrdinalIgnoreCase );

static IEnumerable<ContentFile> GetWwwrootContentFiles( DirectoryInfo wwwrootDir )
{
	FileInfo[] allFiles = wwwrootDir.GetFiles( "*", SearchOption.AllDirectories );

	return allFiles
		.Where( fi => _contentFileExts.Contains( fi.Extension ) )
		.Where( fi => !fi.Name.EndsWith( ".d.ts" ) )
		.Select( fi => new ContentFile( wwwrootDir, fi ) );
}


// #>