import { ArrowRight } from "lucide-react"
import { useMemo } from "react"
import { Link } from "react-router-dom"
import { Button } from "@/components/ui/Button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/Card"
import { MarkdownRenderer } from "@/components/ui/Markdown"

export default function QuickStartSection() {
  const quickStartSnippets = useMemo(
    () => ({
      install: ["```bash", "dotnet add package RazorConsole.Core", "```"].join("\n"),
      projectFile: [
        "```xml",
        '<Project Sdk="Microsoft.NET.Sdk.Razor">',
        "    <!-- your project settings -->",
        "</Project>",
        "```",
      ].join("\n"),
      counter: [
        "```razor",
        "// Counter.razor",
        "@using RazorConsole.Components",
        "",
        "<Columns>",
        "    <p>Current count</p>",
        '    <Markup Content="@currentCount.ToString()" ',
        '            Foreground="@Spectre.Console.Color.Green" />',
        "</Columns>",
        '<TextButton Content="Click me"',
        '            OnClick="IncrementCount"',
        '            BackgroundColor="@Spectre.Console.Color.Grey"',
        '            FocusedColor="@Spectre.Console.Color.Blue" />',
        "",
        "@code {",
        "    private int currentCount = 0;",
        "    private void IncrementCount() => currentCount++;",
        "}",
        "```",
      ].join("\n"),
    }),
    []
  )
  return (
    <Card className="mb-16">
      <CardHeader>
        <CardTitle>Quick Start</CardTitle>
        <CardDescription>Get up and running in minutes</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div>
          <h3 className="mb-2 font-semibold">1. Install the package</h3>
          <MarkdownRenderer content={quickStartSnippets.install} />
        </div>
        <div>
          <h3 className="mb-2 font-semibold">2. Update your project file</h3>
          <MarkdownRenderer content={quickStartSnippets.projectFile} />
        </div>
        <div>
          <h3 className="mb-2 font-semibold">3. Create your first component</h3>
          <MarkdownRenderer content={quickStartSnippets.counter} />
        </div>
        <Link to="/docs#quick-start">
          <Button className="gap-2">
            View Full Tutorial
            <ArrowRight className="h-4 w-4" />
          </Button>
        </Link>
      </CardContent>
    </Card>
  )
}
