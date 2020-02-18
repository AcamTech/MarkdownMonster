﻿#region License
/*
 **************************************************************
 *  Author: Rick Strahl 
 *          © West Wind Technologies, 2016
 *          http://www.west-wind.com/
 * 
 * Created: 04/28/2016
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 **************************************************************  
*/
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ControlzEx.Standard;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Renderers;
using Markdig.Syntax;
using Westwind.Utilities;
using Microsoft.DocAsCode.MarkdigEngine.Extensions;

namespace MarkdownMonster
{

    /// <summary>
    /// Wrapper around the CommonMark.NET parser that provides a cached
    /// instance of the Markdown parser. Hooks up custom processing.
    /// </summary>
    public class MarkdownParserDocFxMarkdig : MarkdownParserBase
    {
        protected MarkdownPipeline Pipeline;
        protected bool UsePragmaLines;

        

        public MarkdownParserDocFxMarkdig(bool usePragmaLines = false)
        {
            UsePragmaLines = usePragmaLines;
            //var builder = CreatePipelineBuilder();
            //Pipeline = builder.Build();            
        }

        /// <summary>
        /// Parses the actual markdown down to html
        /// </summary>
        /// <param name="markdown"></param>
        /// <returns></returns>        
        public override string Parse(string markdown)
        {
            var options = mmApp.Configuration.MarkdownOptions;

            var builder = new MarkdownPipelineBuilder();

            var errors = Array.Empty<string>();
            var tokens = new Dictionary<string, string>();
            var files = new Dictionary<string, string>();

            var actualErrors = new List<string>();
            var actualDependencies = new HashSet<string>();

            var context = new MarkdownContext(
                getToken: key => tokens.TryGetValue(key, out var value) ? value : null,
                logInfo: (a, b, c, d) => { },
                logSuggestion: Log("suggestion"),
                logWarning: Log("warning"),
                logError: Log("error"),
                readFile: ReadFile);

            string filePath = "test.md";
            files.Add(filePath, markdown);


            builder = builder.UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
                .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
                .UseMediaLinks()
                .UsePipeTables()
                .UseAutoLinks()
                .UseHeadingIdRewriter()
                .UseIncludeFile(context)
                .UseCodeSnippet(context)
                .UseDFMCodeInfoPrefix()
                .UseQuoteSectionNote(context)
                .UseXref()
                .UseEmojiAndSmiley(false)
                .UseTabGroup(context)
                .UseMonikerRange(context)
                .UseInteractiveCode()
                .UseRow(context)
                .UseNestedColumn(context)
                .UseTripleColon(context)
                .UseNoloc();

                   

                builder = RemoveUnusedExtensions(builder);
                builder = builder
                    .UseYamlFrontMatter()
                    .UseLineNumber();

                if(options.NoHtml)
                    builder = builder.DisableHtml();

                if (UsePragmaLines)
                    builder = builder.UsePragmaLines();

            var pipeline = builder.Build();

            string html;
            try
            {
                using (InclusionContext.PushFile(filePath))
                {
                    html = Markdown.ToHtml(markdown, pipeline);
                }

                html = ParseFontAwesomeIcons(html);
            }
            catch (Exception ex)
            {
                if (markdown.Length > 10000)
                    markdown = markdown.Substring(0, 10000);

                mmApp.Log("Unable to render Markdown Document (docFx)\n" + markdown, ex, logLevel: LogLevels.Warning);
                html = $@"
<h1><i class='fa fa-warning text-error'></i> Unable to render Markdown Document</h1>

<p>
   An error occurred trying to parse the Markdown document to HTML:
</p>

<b style='font-size: 1.2em'>{ex.Message}</b>

<p>
    <a id='hrefShow' href='#0' style='font-size: 0.8em; font-weight: normal'>more info...</a>
</p>

<div id='detail' style='display:none'>


<p style='margin-top: 2em'>
    <b>Markdown Parser</b>: {options.MarkdownParserName}
</p>


<pre style='padding: 8px; background: #eee; color: #333' >{System.Net.WebUtility.HtmlEncode(StringUtils.NormalizeIndentation(ex.StackTrace))}</pre>
</div>

<script>
$('#hrefShow').click(function () {{ $('#detail').show(); }});
</script>
";
                return html;
            }

            if (mmApp.Configuration.MarkdownOptions.RenderLinksAsExternal)
                html = ParseExternalLinks(html);

            if (!mmApp.Configuration.MarkdownOptions.AllowRenderScriptTags)
                html = HtmlUtils.SanitizeHtml(html);

            Debug.WriteLine(html);

            return html;

            
            MarkdownContext.LogActionDelegate Log(string level)
            {
                return (code, message, origin, line) => actualErrors.Add(code);
            }

            (string content, object file) ReadFile(string path, object relativeTo, MarkdownObject origin)
            {

                string key;
                var relativePath = relativeTo as string;
                if(string.IsNullOrEmpty(relativePath))
                    key = path;
                else
                    key = Path.Combine(Path.GetDirectoryName(relativePath), path).Replace('\\', '/');
            
                
                if (path.StartsWith("~/"))
                {
                   path = path.Substring(2);
                   key = path;
               }

               actualDependencies.Add(path);

               files.TryGetValue(key, out var value);
               if (value == null)
               {
                   try
                   {
                       value = File.ReadAllText(key)?.Trim();
                   }catch { }
               }

               if (value == null)
                   return (null, null);

               return (value, key as object);
            }
        }

        /// <summary>
        /// Builds the Markdig processing pipeline and returns a builder.
        /// Use this method to override any custom pipeline addins you want to
        /// add or append. 
        /// 
        /// Note you can also add addins using options.MarkdigExtensions which
        /// use MarkDigs extension syntax using commas instead of +.
        /// </summary>
        /// <param name="options"></param>
        /// <param name="builder"></param>
        /// <returns></returns>
        protected virtual MarkdownPipelineBuilder BuildPipeline(MarkdownOptionsConfiguration options, MarkdownPipelineBuilder builder)
        {
            return builder;
        }

        private static MarkdownPipelineBuilder RemoveUnusedExtensions(MarkdownPipelineBuilder pipeline)
        {
            pipeline.Extensions.RemoveAll(extension => extension is CustomContainerExtension);
            return pipeline;
        }

        /// <summary>
        /// Create the entire Markdig pipeline and return the completed
        /// ready to process builder.
        /// </summary>
        /// <returns></returns>
        public  virtual MarkdownPipelineBuilder CreatePipelineBuilder()
        {
            var options = mmApp.Configuration.MarkdownOptions;
            var builder = new MarkdownPipelineBuilder();

            try
            {
                builder = BuildPipeline(options, builder);
            }
            catch (ArgumentException ex)
            {
                mmApp.Log($"Failed to build pipeline: {ex.Message}", ex);
            }

            return builder;
        }

        protected virtual IMarkdownRenderer CreateRenderer(TextWriter writer)
        {
            return new HtmlRenderer(writer);
        }
    }
}
