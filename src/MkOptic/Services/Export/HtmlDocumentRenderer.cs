using System.Net;
using Markdig;
using Markdig.Syntax;

namespace MkOptic.Services.Export;

internal static class HtmlDocumentRenderer
{
    public static string Render(MarkdownDocument document, string title, MarkdownPipeline pipeline) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src data: https: http:; base-uri 'none'; form-action 'none'">
          <title>{{WebUtility.HtmlEncode(title)}}</title>
          <style>
            :root { color-scheme: light; font-family: system-ui, sans-serif; color: #202124; background: #fff; }
            body { max-width: 900px; margin: 40px auto; padding: 0 24px; line-height: 1.65; overflow-wrap: anywhere; }
            h1, h2, h3, h4, h5, h6 { line-height: 1.25; margin-top: 1.5em; break-after: avoid; }
            h1, h2 { border-bottom: 1px solid #ddd; padding-bottom: .3em; }
            a { color: #0969da; } img { max-width: 100%; height: auto; }
            pre, code { font-family: ui-monospace, Consolas, monospace; background: #f4f5f6; border-radius: 4px; }
            code { padding: .15em .3em; } pre { padding: 16px; white-space: pre-wrap; } pre code { padding: 0; }
            blockquote { margin-left: 0; padding-left: 1em; border-left: 4px solid #ccd; color: #555; }
            table { border-collapse: collapse; width: 100%; margin: 1em 0; }
            th, td { border: 1px solid #ccc; padding: 6px 12px; } th { background: #f4f5f6; }
            hr { border: 0; border-top: 1px solid #ccc; } .task-list-item { list-style: none; }
            @media print { body { max-width: none; margin: 0; } @page { size: A4; margin: 20mm; } }
          </style>
        </head>
        <body>
        {{Markdown.ToHtml(document, pipeline)}}
        </body>
        </html>
        """;
}
