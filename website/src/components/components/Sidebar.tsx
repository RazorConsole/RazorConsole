import type { ComponentInfo } from "@/types/components/componentInfo"
import { cn } from "@/lib/utils"
import { NavLink } from "react-router-dom"

interface Props {
  groupedComponents: Record<string, ComponentInfo[]>
  categories: string[]
}

const Sidebar: React.FC<Props> = ({ groupedComponents, categories }) => (
  <>
    <div className="pb-4">
      <h4 className="mb-1 rounded-md px-2 py-1 text-sm font-semibold">Getting Started</h4>
      <div className="grid grid-flow-row auto-rows-max text-sm">
        <NavLink
          to="/components"
          end
          className={({ isActive }) =>
            cn(
              "group text-muted-foreground flex w-full items-center rounded-md border border-transparent px-2 py-1 hover:underline",
              isActive ? "text-foreground font-medium text-blue-600 dark:text-blue-400" : ""
            )
          }
        >
          Overview
        </NavLink>
      </div>
    </div>
    {categories.map((category) => (
      <div key={category} className="pb-4">
        <h4 className="mb-1 rounded-md px-2 py-1 text-sm font-semibold">{category}</h4>
        <div className="grid grid-flow-row auto-rows-max text-sm">
          {groupedComponents[category]?.map((component) => (
            <NavLink
              key={component.name}
              to={`/components/${component.name.toLowerCase()}`}
              className={({ isActive }) =>
                cn(
                  "group text-muted-foreground flex w-full items-center rounded-md border border-transparent px-2 py-1 text-left hover:underline",
                  isActive ? "font-medium text-blue-600 dark:text-blue-400" : ""
                )
              }
            >
              {component.name}
            </NavLink>
          ))}
        </div>
      </div>
    ))}
  </>
)

export default Sidebar
