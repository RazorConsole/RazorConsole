import manifest from "./official-apps.generated.json"
import type { ShowcaseProject } from "./showcase"

export interface OfficialApp extends ShowcaseProject {
  slug: string
  packageId: string
  commandName: string
  repositoryUrl: string
  sourceUrl: string
}

const mediaByPackageId: Record<string, Pick<ShowcaseProject, "videoUrl" | "imageUrls">> = {
  "RazorConsole.Gallery": { videoUrl: "showcase/gallery-demo.mp4" },
  "RazorConsole.Htop": { videoUrl: "showcase/htop-demo.mp4" },
  "RazorConsole.Snake": { videoUrl: "showcase/snake-demo.mp4" },
  "RazorConsole.TankBattle": { videoUrl: "showcase/tank-battle-demo.mp4" },
}

const toSlug = (title: string) =>
  title
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")

export const officialApps: OfficialApp[] = manifest.apps.map((app) => {
  const appName = app.packageId.split(".").at(-1)
  const unixInstaller =
    "curl -fsSL https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.sh | sh -s --"
  const windowsInstaller =
    "& ([scriptblock]::Create((irm https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.ps1)))"
  return {
    slug: toSlug(app.title),
    name: app.title,
    description: app.description,
    packageId: app.packageId,
    commandName: app.commandName,
    repositoryUrl: app.repositoryUrl,
    sourceUrl: `https://github.com/RazorConsole/RazorConsole/tree/main/${app.project.replace(/\/[^/]+\.csproj$/, "")}`,
    website: app.projectUrl,
    downloadUrl: "https://github.com/RazorConsole/RazorConsole/releases/latest",
    installCommands: [
      {
        label: "macOS / Linux",
        command: `${unixInstaller} --app ${appName}`,
        nightlyCommand: `${unixInstaller} --app ${appName} --channel nightly`,
      },
      {
        label: "Windows PowerShell",
        command: `${windowsInstaller} -App ${appName}`,
        nightlyCommand: `${windowsInstaller} -App ${appName} -Channel Nightly`,
      },
    ],
    ...mediaByPackageId[app.packageId],
  }
})

export const getOfficialApp = (slug: string | undefined) =>
  officialApps.find((app) => app.slug === slug)
