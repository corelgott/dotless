using System;
using System.Text;
using System.Text.RegularExpressions;
using dotless.Core.Plugins;

namespace dotless.Core
{
    using System.Collections.Generic;
    using System.Linq;
    using Exceptions;
    using Loggers;
    using Parser.Infrastructure;

    public class LessEngine : ILessEngine
    {
        public Parser.Parser Parser { get; set; }
        public ILogger Logger { get; set; }
        public bool Compress { get; set; }
        public bool Debug { get; set; }
        public bool DisableVariableRedefines { get; set; }
        public bool KeepFirstSpecialComment { get; set; }
        public Env Env { get; set; }
        public IEnumerable<IPluginConfigurator> Plugins { get; set; }
        public bool LastTransformationSuccessful { get; private set; }

        public LessEngine(Parser.Parser parser, ILogger logger, bool compress, bool debug, bool disableVariableRedefines, bool keepFirstSpecialComment, IEnumerable<IPluginConfigurator> plugins)
        {
            Parser = parser;
            Logger = logger;
            Compress = compress;
            Debug = debug;
            DisableVariableRedefines = disableVariableRedefines;
            Plugins = plugins;
            KeepFirstSpecialComment = keepFirstSpecialComment;
        }

        public LessEngine(Parser.Parser parser, ILogger logger, bool compress, bool debug)
            : this(parser, logger, compress, debug, false, false, null)
        {
        }

        public LessEngine(Parser.Parser parser, ILogger logger, bool compress, bool debug, bool disableVariableRedefines)
            : this(parser, logger, compress, debug, disableVariableRedefines, false, null)
        {
        }

        public LessEngine(Parser.Parser parser)
            : this(parser, new ConsoleLogger(LogLevel.Error), false, false, false, false, null)
        {
        }

        public LessEngine()
            : this(new Parser.Parser())
        {
        }

        public string TransformToCss(string source, string fileName)
        {
            return TransformToCss(source, fileName, null);
        }
        public string TransformToCss(string source, string fileName, StringBuilder sourceMap)
        {
            try
            {
                var tree = Parser.Parse(source, fileName);

                var env = Env ?? new Env { Compress = Compress, Debug = Debug, KeepFirstSpecialComment = KeepFirstSpecialComment, DisableVariableRedefines = DisableVariableRedefines };

                if (Plugins != null)
                {
                    foreach (IPluginConfigurator configurator in Plugins)
                    {
                        env.AddPlugin(configurator.CreatePlugin());
                    }
                }

                var css = tree.ToCSS(env);                

                if (sourceMap != null) {           
                    // post process the css to get rid of the markers
                    css = this.PostProcessSourceMap(env, ref css);
                    // add & generate the source map
                    sourceMap.Append(env.SourceMap.GenerateSourceMap());
                }

                LastTransformationSuccessful = true;
                return css;
            }
            catch (ParserException e)
            {
                LastTransformationSuccessful = false;
                Logger.Error(e.Message);
            }

            return "";
        }

        /// <summary>
        /// post processes the final css result. It create's added each chunk to the source map and removes the markers that are used during the generation
        /// </summary>
        /// <param name="env">the engines environment</param>
        /// <param name="css">the css-text that was generated by the engine containing the source markers</param>
        /// <returns>a cleaned up version of the css without the markers</returns>
        protected string PostProcessSourceMap(Env env, ref string css) {
            if (env == null) throw new ArgumentNullException("env");

            // the position to start from
            int offset = 0;

            // pattern to match the source tags against
            var regex = new Regex(@"\/\*@source:\""(.+?)\""\[(\d+):(\d+)\]\*\/", RegexOptions.Compiled);       

            // var to hold the next match
            Match match = null;
                   
            // create a buffer to hold the cleand up css
            var buffer = new StringBuilder(css);
            
            do {
                var daCss = buffer.ToString();
                // look for the next marker in the string
                match = regex.Match(daCss, offset);
                if (match.Success) {            
                    // get the markers position
                    int markerPos    = match.Index;               
 
                    // get the entry's column in row
                    var column     = markerPos - Math.Max(daCss.LastIndexOf("\n", markerPos), 0) - 1;
                    
                    // get the linenumber
                    var line       = daCss.Substring(0, markerPos).Count(c => c == '\n');

                    // generate a source mapping-fragment with the found info
                    var theFrag = new SourceMapping.SourceFragment {
                        GeneratedColumn = column,
                        GeneratedLine   = line,
                        SourceFile      = match.Groups[1].Value,                    
                        SourceLine      = int.Parse(match.Groups[2].Value),
                        SourceColumn    = int.Parse(match.Groups[3].Value),
                    };

                    // add the fragment to the list
                    env.SourceMap.AddSourceFragment(theFrag);
                    
                 
                    offset = markerPos;
                    // remove the marker from the buffer
                    buffer.Remove(markerPos, match.Value.Length);
                }
            } while(match.Success);

            return buffer.ToString();
        }

        public IEnumerable<string> GetImports()
        {
            return Parser.Importer.Imports.Distinct();
        }

        public void ResetImports()
        {
            Parser.Importer.Imports.Clear();
        }

    }
}