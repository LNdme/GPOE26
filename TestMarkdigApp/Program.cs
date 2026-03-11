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
            
        var markdown = @"
$$
f(x) = \int_{-\infty}^\infty \hat f(\xi)\,e^{2 \pi i \xi x} \,d\xi
$$

Equation: $E = mc^2$";
        var html = Markdown.ToHtml(markdown, pipeline);
        Console.WriteLine(html);
    }
}
