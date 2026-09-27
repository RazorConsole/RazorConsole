import manifest from "./official-apps.generated.json"
import type { ShowcaseProject } from "./showcase"

const mediaByPackageId: Record<string, Pick<ShowcaseProject, "videoUrl" | "imageUrls">> = {
  "RazorConsole.Gallery": { videoUrl: "showcase/gallery-demo.mp4" },
  "RazorConsole.Snake": { videoUrl: "showcase/snake-demo.mp4" },
}

export const officialApps: ShowcaseProject[] = manifest.apps.map((app) => {
  const appName = app.packageId.split(".").at(-1)
  const unixInstaller =
    "curl -fsSL https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.sh | sh -s --"
  const windowsInstaller =
    "& ([scriptblock]::Create((irm https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.ps1)))"
  return {
    name: app.title,
    description: app.description,
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
