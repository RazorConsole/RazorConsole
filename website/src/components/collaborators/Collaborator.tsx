import type { Collaborator } from "@/data/collaborators"
import { Github, Linkedin, Globe } from "lucide-react"
import { Button } from "@/components/ui/Button"
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from "@/components/ui/Card"
function XIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M18.244 2.25h3.308l-7.227 8.26 8.502 11.24H16.17l-5.214-6.817L4.99 21.75H1.68l7.73-8.835L1.254 2.25H8.08l4.713 6.231zm-1.161 17.52h1.833L7.084 4.126H5.117z" />
    </svg>
  )
}

export default function Collaborator({ collaborator }: { collaborator: Collaborator }) {
  return (
    <Card key={collaborator.github} className="flex flex-col">
      <CardHeader className="text-center">
        <div className="mx-auto mb-4">
          <img
            src={collaborator.avatar ?? `https://github.com/${collaborator.github}.png`}
            alt={`${collaborator.name}'s avatar`}
            className="h-24 w-24 rounded-full border-2 border-slate-200 dark:border-slate-700"
            loading="lazy"
            onError={(e) => {
              e.currentTarget.src = `https://ui-avatars.com/api/?name=${encodeURIComponent(collaborator.name)}&background=random`
            }}
          />
        </div>
        <CardTitle className="text-xl">{collaborator.name}</CardTitle>
        <CardDescription className="text-sm font-medium text-blue-600 dark:text-blue-400">
          {collaborator.role}
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-1 flex-col">
        <p className="flex-1 text-center text-sm text-slate-600 dark:text-slate-400">
          {collaborator.bio}
        </p>
        <div className="mt-4 flex flex-wrap justify-center gap-2">
          <a
            href={`https://github.com/${collaborator.github}`}
            target="_blank"
            rel="noopener noreferrer"
          >
            <Button variant="outline" size="sm" className="gap-2">
              <Github className="h-4 w-4" />@{collaborator.github}
            </Button>
          </a>
          {collaborator.x && (
            <a
              href={`https://x.com/${collaborator.x}`}
              target="_blank"
              rel="noopener noreferrer"
              aria-label={`${collaborator.name} on X`}
            >
              <Button variant="outline" size="sm" className="gap-2">
                <XIcon className="h-4 w-4" />
                <span className="sr-only">X profile</span>
              </Button>
            </a>
          )}
          {collaborator.linkedin && (
            <a
              href={`https://linkedin.com/in/${collaborator.linkedin}`}
              target="_blank"
              rel="noopener noreferrer"
              aria-label={`${collaborator.name} on LinkedIn`}
            >
              <Button variant="outline" size="sm" className="gap-2">
                <Linkedin className="h-4 w-4" />
                <span className="sr-only">LinkedIn profile</span>
              </Button>
            </a>
          )}
          {collaborator.website && /^https?:\/\//i.test(collaborator.website) && (
            <a
              href={collaborator.website}
              target="_blank"
              rel="noopener noreferrer"
              aria-label={`${collaborator.name}'s website`}
            >
              <Button variant="outline" size="sm" className="gap-2">
                <Globe className="h-4 w-4" />
                <span className="sr-only">Personal website</span>
              </Button>
            </a>
          )}
        </div>
      </CardContent>
    </Card>
  )
}
