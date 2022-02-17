<Query Kind="Program">
  <NuGetReference>Microsoft.CodeAnalysis</NuGetReference>
  <NuGetReference>Sfcd.Utility</NuGetReference>
  <NuGetReference>Sfcd.Utility.Data</NuGetReference>
  <Namespace>Microsoft.CodeAnalysis</Namespace>
  <Namespace>Microsoft.CodeAnalysis.Classification</Namespace>
  <Namespace>Microsoft.CodeAnalysis.CodeActions</Namespace>
  <Namespace>Microsoft.CodeAnalysis.CodeFixes</Namespace>
  <Namespace>Microsoft.CodeAnalysis.CodeRefactorings</Namespace>
  <Namespace>Microsoft.CodeAnalysis.CodeStyle</Namespace>
  <Namespace>Microsoft.CodeAnalysis.CSharp</Namespace>
  <Namespace>Microsoft.CodeAnalysis.CSharp.Formatting</Namespace>
  <Namespace>Microsoft.CodeAnalysis.CSharp.Syntax</Namespace>
  <Namespace>Microsoft.CodeAnalysis.Diagnostics</Namespace>
  <Namespace>Microsoft.CodeAnalysis.Diagnostics.Telemetry</Namespace>
  <Namespace>Microsoft.CodeAnalysis.Differencing</Namespace>
  <Namespace>Microsoft.CodeAnalysis.Editing</Namespace>
  <Namespace>Microsoft.CodeAnalysis.Emit</Namespace>
  <Namespace>Microsoft.CodeAnalysis.FindSymbols</Namespace>
  <Namespace>Microsoft.CodeAnalysis.FlowAnalysis</Namespace>
  <Namespace>Microsoft.CodeAnalysis.Formatting</Namespace>
  <Namespace>Microsoft.CodeAnalysis.Host</Namespace>
  <Namespace>Microsoft.CodeAnalysis.Host.Mef</Namespace>
  <Namespace>Microsoft.CodeAnalysis.Operations</Namespace>
  <Namespace>Microsoft.CodeAnalysis.Options</Namespace>
  <Namespace>Microsoft.CodeAnalysis.Recommendations</Namespace>
  <Namespace>Microsoft.CodeAnalysis.Rename</Namespace>
  <Namespace>Microsoft.CodeAnalysis.Simplification</Namespace>
  <Namespace>Microsoft.CodeAnalysis.Tags</Namespace>
  <Namespace>Microsoft.CodeAnalysis.Text</Namespace>
  <Namespace>Sfcd.Utility</Namespace>
  <Namespace>Sfcd.Utility.CaseExceptions</Namespace>
  <Namespace>Sfcd.Utility.CollectionAdapters</Namespace>
  <Namespace>Sfcd.Utility.Collections</Namespace>
  <Namespace>Sfcd.Utility.Csv</Namespace>
  <Namespace>Sfcd.Utility.Data</Namespace>
  <Namespace>Sfcd.Utility.IO</Namespace>
  <Namespace>Sfcd.Utility.Linq</Namespace>
  <Namespace>Sfcd.Utility.Objects</Namespace>
  <Namespace>Sfcd.Utility.Processes</Namespace>
  <Namespace>System.Composition</Namespace>
  <Namespace>System.Composition.Convention</Namespace>
  <Namespace>System.Composition.Hosting</Namespace>
  <Namespace>System.Composition.Hosting.Core</Namespace>
  <Namespace>System.Globalization</Namespace>
  <Namespace>System.IO.Pipelines</Namespace>
  <Namespace>System.Net</Namespace>
  <Namespace>System.Net.Http</Namespace>
  <Namespace>System.Net.Http.Headers</Namespace>
  <Namespace>System.Threading.Tasks</Namespace>
  <Namespace>System.Diagnostics.CodeAnalysis</Namespace>
  <IncludePredicateBuilder>true</IncludePredicateBuilder>
  <UseNoncollectibleLoadContext>true</UseNoncollectibleLoadContext>
</Query>

#load ".\ExtractControllerActionRoutes.cs"

void Main()
{
	const String CONTROLLERS_DIR_PATH_AJAX = @"C:\git\your-project\Controllers\Ajax";
	const String CONTROLLERS_DIR_PATH_MVC  = @"C:\git\your-project\Controllers\Mvc"; // i.e. exclude "Api/"
	const String OUTPUT_FILE_PATH	       = @"C:\git\your-project\Generated\AllActions_GENERATED.cs";
	
	FileInfo outputTemplateTextFile = new FileInfo( Util.CurrentQueryPath ).Parent().CombinePathToFile( "OutputTemplate.txt" );
	String outputTemplateText = File.ReadAllText( outputTemplateTextFile.FullName );
	
	DirectoryInfo dir1 = new DirectoryInfo( CONTROLLERS_DIR_PATH_AJAX );
	DirectoryInfo dir2 = new DirectoryInfo( CONTROLLERS_DIR_PATH_MVC  );
	
	IReadOnlyList<ActionRoute> list = ControllerActionRouteExtractor.ExtractActionRoutes( dir1, dir2 );
	
//	list.Dump();
	
	ControllerActionRouteExtractor.WriteOutput( outputFilePath: new FileInfo( OUTPUT_FILE_PATH ), outputTemplateText, list );
	
	list.Count.Dump( "Saved this many routes as UrlHelper extensions:" );
}
