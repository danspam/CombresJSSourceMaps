using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web;
using System.Web.Hosting;
using Combres;
using Microsoft.Ajax.Utilities;

namespace CombresJSSourceMaps
{
    public class SourceMapMinifier : IResourceMinifier
    {
        static readonly object SourceMapLock = new object();

        /// <summary>
        /// Convert <c>new Object()</c> to <c>{}</c>, <c>new Array()</c> to <c>[]</c>,
        /// <c>new Array(1,2,3,4,5)</c> to <c>[1,2,3,4,5]</c>, and <c>new Array("foo")</c> becomes <c>["foo"]</c>.
        ///
        /// Default is <c>true</c>.
        /// </summary>
        public bool? CollapseToLiteral { get; set; }

        /// <summary>
        /// Normally an eval statement can contain anything, including references to local variables and functions.
        /// Because of that, if we encounter an eval statement, that scope and all parent scopes cannot take advantage
        /// of local variable and function renaming because things could break when the eval is evaluated and
        /// the references are looked up. However, sometimes the developer knows that he's not referencing local
        /// variables in his eval (like when only evaluating JSON objects), and this switch can be set to true to make
        /// sure you get the maximum crunching. Very dangerous setting; should only be used when you are certain
        /// that the eval won't be referencing local variables or functions.
        /// </summary>
        public bool? EvalsAreSafe { get; set; }

        /// <summary>
        /// There was one quirk that Safari on the Mac (not the PC) needed that we were crunching out:
        /// throw statements always seem to require a terminating semicolon.
        /// Another Safari-specific quirk is that an if-statement only contains a function declaration,
        /// Safari will throw a syntax error if the declaration isn't surrounded with curly-braces.
        /// Basically, if you want your code to always work in Safari, set this to true.
        /// If you don't care about Safari, it might save a few bytes.
        ///
        /// Default is <c>true</c>.
        /// </summary>
        public bool? MacSafariQuirks { get; set; }

        /// <summary>
        /// Treat the catch variable as if it's local to the function scope.
        ///
        /// Default is <c>true</c>.
        /// </summary>
        public bool? CatchAsLocal { get; set; }

        /// <summary>
        /// Renaming of locals. There are a couple settings:
        /// - KeepAll is the default and doesn't rename variables or functions at all.
        /// - CrunchAll renames everything it can.
        /// - KeepLocalizationVars renames everything it can except for variables starting with L_.
        ///   Those are left as-is so localization efforts can continue on the crunched code.
        /// </summary>
        public string LocalRenaming { get; set; }

        /// <summary>
        /// <c>SingleLine</c> crunches everything to a single line.
        /// <c>MultipleLines</c> breaks the crunched code into multiple lines for easier reading.
        ///
        /// Default is <c>SingleLine</c>.
        /// </summary>
        public string OutputMode { get; set; }

        /// <summary>
        /// Removes unreferenced local functions (not global functions, though),
        /// unreferenced function parameters, quotes around object literal field names
        /// that won't be confused with reserved words, and it does some interesting things
        /// with switch statements.
        ///
        /// Default is <c>true</c>.
        /// </summary>
        public bool? RemoveUnneededCode { get; set; }

        /// <summary>
        /// Removes "debugger" statements, any calls into certain namespaces like
        /// $Debug, Debug, Web.Debug or Msn.Debug. also strips calls to the WAssert function.
        ///
        /// Default is <c>true</c>.
        /// </summary>
        public bool? StripDebugStatements { get; set; }

        /// <summary>
        /// Path where the sourcemap will be saved. The sourcemap file will be named [ResourceSetName].js.map
        /// </summary>
        public string SourceMapOutputPath { get; set; }

        /// <inheritdoc cref="IResourceMinifier.Minify" />
        public string Minify(Settings settings, ResourceSet resourceSet, string combinedContent)
        {
            var localRenaming = (LocalRenaming)ConvertToEnum(LocalRenaming, typeof(LocalRenaming), Microsoft.Ajax.Utilities.LocalRenaming.CrunchAll);
            var outputMode = (OutputMode)ConvertToEnum(OutputMode, typeof(OutputMode), Microsoft.Ajax.Utilities.OutputMode.SingleLine);
            var codeSettings = new CodeSettings
            {
                MacSafariQuirks = MacSafariQuirks == null || MacSafariQuirks.Value,
                CollapseToLiteral = CollapseToLiteral == null || CollapseToLiteral.Value,
                LocalRenaming = localRenaming,
                OutputMode = outputMode,
                RemoveUnneededCode = RemoveUnneededCode == null || RemoveUnneededCode.Value,
                StripDebugStatements = StripDebugStatements == null || StripDebugStatements.Value
            };

            //bypass source maps if debug
            if (resourceSet.DebugEnabled)
            {
                return new Minifier().MinifyJavaScript(combinedContent, codeSettings);
            }

            //ensure setting has be created
            if (string.IsNullOrEmpty(SourceMapOutputPath))
            {
                throw new NullReferenceException("No SourceMapOutputPath setting specified.");
            }

            //regenerate the combined content, adding in the source directive.
            combinedContent = CombineResourcesWithSourceDirective(resourceSet.Resources);

            return MinifyWithSourceMap(settings, resourceSet, codeSettings, combinedContent);
        }

        private string MinifyWithSourceMap(Settings settings, ResourceSet resourceSet, CodeSettings codeSettings, string combinedContent)
        {
            var utf8 = new UTF8Encoding(false);
            var outputBuilder = new StringBuilder();
            var outputPath = String.Concat(settings.Url.TrimEnd('/'), "/", resourceSet.Name);
            var mapFile = String.Concat(SourceMapOutputPath.TrimEnd('/'), "/", string.Concat(resourceSet.Name, ".js.map"));
            var mapFileFullPath = HostingEnvironment.MapPath(mapFile);

            if (string.IsNullOrEmpty(mapFileFullPath))
            {
                throw new NullReferenceException("mapFileFullPath is null");
            }

            //there is file writing in this so lock for threading
            lock (SourceMapLock)
            {
                //Source maps are created by minifying the files individually and combining the output
                //before adding the sourcemap url to the end.
                using (var outputWriter = new StringWriter(outputBuilder))
                {
                    //using (var mapWriter = new StreamWriter(sourceMapStream, utf8))
                    using (var mapWriter = new StreamWriter(mapFileFullPath, false, utf8))
                    {
                        using (var sourceMap = new V3SourceMap(mapWriter))
                        {
                            //set the sourcemap as part of minification settings
                            codeSettings.SymbolsMap = sourceMap;
                            codeSettings.TermSemicolons = true;

                            //initialize the sourcemap
                            sourceMap.StartPackage(VirtualPathUtility.ToAbsolute(outputPath),
                                VirtualPathUtility.ToAbsolute(mapFile));

                            var minifier = new Minifier();
                            outputWriter.Write(minifier.MinifyJavaScript(combinedContent, codeSettings));

                            //sourcemap complete
                            sourceMap.EndPackage();
                            //write the sourcemap url to the end of the minified output
                            sourceMap.EndFile(outputWriter, "\r\n");
                        }
                    }
                }
            }

            return outputBuilder.ToString();
        }

        private static string CombineResourcesWithSourceDirective(IEnumerable<Resource> resources)
        {
            var concatOutput = new StringBuilder();

            foreach (var resource in resources)
            {
                if (resource.Mode == ResourceMode.Dynamic)
                {
                    throw new InvalidOperationException("Dynamic resources are not supported with ajax min sourcemaps.");
                }

                //determine actual filepath so we can read contents
                var fullPath = HostingEnvironment.MapPath(resource.Path);
                if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
                {
                    throw new FileNotFoundException(resource.Path);
                }

                //add a source directive comment - see https://ajaxmin.codeplex.com/discussions/446616
                //this enables the sourcemap to know about all the files
                // Format: "///#SOURCE line col path"
                concatOutput.Append(";///#SOURCE 1 1 ");
                concatOutput.AppendLine(VirtualPathUtility.ToAbsolute(resource.Path));

                //add content
                concatOutput.AppendLine(File.ReadAllText(fullPath));
            }

            return concatOutput.ToString();
        }

        private static object ConvertToEnum(string value, Type targetType, object defaultValue)
        {
            try
            {
                var result = Enum.Parse(targetType, value, true);
                return result ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

    }
}