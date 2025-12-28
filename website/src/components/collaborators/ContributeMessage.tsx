import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card"
import { Button } from "@/components/ui/Button"

export default function ContributeMessage() {
  return (
    <div className="mt-16 text-center">
      <Card className="mx-auto max-w-2xl">
        <CardHeader>
          <CardTitle className="text-xl">Want to Contribute?</CardTitle>
        </CardHeader>
        <CardContent>
          <p className="mb-4 text-slate-600 dark:text-slate-400">
            RazorConsole is an open-source project and we welcome contributions from the community!
          </p>
          <div className="flex flex-wrap justify-center gap-4">
            <a
              href="https://github.com/RazorConsole/RazorConsole/blob/main/CONTRIBUTING.md"
              target="_blank"
              rel="noopener noreferrer"
            >
              <Button variant="outline" className="gap-2">
                Contributing Guide
              </Button>
            </a>
            <a href="https://discord.gg/DphHAnJxCM" target="_blank" rel="noopener noreferrer">
              <Button variant="secondary" className="gap-2">
                Join Discord
              </Button>
            </a>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
