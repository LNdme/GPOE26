using System;
using Markdig;

class Program
{
    static void Main()
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseMathematics()
            .Build();
            
        var html = Markdown.ToHtml("Equation: $E = mc^2$ and $$ \\frac{1}{2} $$", pipeline);
        Console.WriteLine(html);
    }
}
