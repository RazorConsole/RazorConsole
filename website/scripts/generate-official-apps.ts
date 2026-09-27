import { execFileSync } from "node:child_process"
import { existsSync, readdirSync, writeFileSync } from "node:fs"
import { homedir } from "node:os"
import { dirname, join, relative } from "node:path"
import { fileURLToPath } from "node:url"

interface MsBuildResult {
  Properties: Record<string, string>
}

const websiteRoot = dirname(dirname(fileURLToPath(import.meta.url)))
const repositoryRoot = dirname(websiteRoot)
const galleryRoot = join(repositoryRoot, "gallery")
const outputPath = join(websiteRoot, "src/data/official-apps.generated.json")
const userDotNet = join(homedir(), ".dotnet", process.platform === "win32" ? "dotnet.exe" : "dotnet")
const dotnet = process.env.DOTNET_HOST_PATH || (existsSync(userDotNet) ? userDotNet : "dotnet")
const propertyNames = [
  "RazorConsolePublishNativeAot",
  "PackageId",
  "Title",
  "Description",
  "PackageProjectUrl",
  "RepositoryUrl",
  "ToolCommandName",
  "AssemblyName",
].join(",")

const projects = readdirSync(galleryRoot, { withFileTypes: true })
  .filter((entry) => entry.isDirectory())
  .flatMap((entry) =>
    readdirSync(join(galleryRoot, entry.name))
      .filter((name) => name.endsWith(".csproj"))
      .map((name) => join(galleryRoot, entry.name, name))
  )
  .sort()

const apps = projects.flatMap((project) => {
  const output = execFileSync(
    dotnet,
    ["msbuild", project, "-nologo", `-getProperty:${propertyNames}`],
    { encoding: "utf8" }
  )
  const properties = (JSON.parse(output) as MsBuildResult).Properties
  if (properties.RazorConsolePublishNativeAot.toLowerCase() !== "true") return []

  for (const required of ["PackageId", "Title", "Description", "AssemblyName"]) {
    if (!properties[required]) {
      throw new Error(`${relative(repositoryRoot, project)} enables RazorConsolePublishNativeAot but has no ${required}.`)
    }
  }

  return [
    {
      packageId: properties.PackageId,
      title: properties.Title,
      description: properties.Description,
      projectUrl: properties.PackageProjectUrl || properties.RepositoryUrl,
      repositoryUrl: properties.RepositoryUrl,
      commandName:
        properties.ToolCommandName || properties.PackageId.replaceAll(".", "-").toLowerCase(),
      assemblyName: properties.AssemblyName,
      project: relative(repositoryRoot, project).replaceAll("\\", "/"),
    },
  ]
})

const duplicate = (key: "packageId" | "commandName") =>
  apps.find((app, index) => apps.findIndex((candidate) => candidate[key] === app[key]) !== index)

const duplicatePackage = duplicate("packageId")
const duplicateCommand = duplicate("commandName")
if (duplicatePackage) throw new Error(`Duplicate PackageId: ${duplicatePackage.packageId}`)
if (duplicateCommand) throw new Error(`Duplicate command name: ${duplicateCommand.commandName}`)
if (apps.length === 0) throw new Error("No official Native AOT apps were discovered.")

const manifest = JSON.stringify({ apps }, null, 2).replaceAll("\n", "\r\n")
writeFileSync(outputPath, `${manifest}\r\n`)
console.log(`Generated ${relative(repositoryRoot, outputPath)} with ${apps.length} apps.`)
