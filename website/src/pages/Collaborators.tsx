import { collaborators } from "@/data/collaborators"
import Collaborator from "@/components/collaborators/Collaborator"
import ContributeMessage from "@/components/collaborators/ContributeMessage"
import type { MetaFunction } from "react-router";

export const meta: MetaFunction = () => [
  { title: "Collaborators | RazorConsole" },
  { name: "description", content: "Meet the incredible people behind RazorConsole framework." }
];

export default function Collaborators() {
  return (
    <div className="min-h-screen bg-linear-to-b from-slate-50 to-white dark:from-slate-950 dark:to-slate-900">
      <div className="container mx-auto px-4 py-16">
        <div className="mb-12 text-center">
          <h1 className="mb-4 text-4xl font-bold tracking-tight text-slate-900 dark:text-slate-50">
            Collaborators
          </h1>
          <p className="mx-auto max-w-2xl text-lg text-slate-600 dark:text-slate-300">
            Meet the people who make RazorConsole possible
          </p>
        </div>

        <div className="mx-auto grid max-w-5xl grid-cols-1 gap-6 md:grid-cols-2 lg:grid-cols-3">
          {collaborators.map((collaborator, index) => (
            <Collaborator collaborator={collaborator} key={index} />
          ))}
        </div>

        <ContributeMessage />
      </div>
    </div>
  )
}
