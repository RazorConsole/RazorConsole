import { Navigate } from "react-router"
import { components } from "@/data/components"
import { ComponentPreview } from "@/components/components/ComponentPreview"
import { cn, getCategoryBadgeColor, getFullSitePath } from "@/lib/utils"
import ApiSection from "@/components/components/ApiSection"
import ParametersTable from "@/components/components/ParametersTable"
import type { MetaFunction, LoaderFunctionArgs } from "react-router";
import { useLoaderData } from "react-router";

export const meta: MetaFunction<typeof loader> = ({ data, location }) => {
  if (!data) return [{ title: "Component Not Found | RazorConsole" }];

  const { component } = data;

  const fullBaseUrl = getFullSitePath();  
  const pageUrl = `${fullBaseUrl}${location.pathname}`;

  const title = `${component.name} Component | RazorConsole`;
  const description = component.description;
  const ogImage = `${fullBaseUrl}/og/${component.name.toLowerCase()}.png`;

  return [
    { title },
    { name: "description", content: description },

    // Open Graph / Facebook
    { property: "og:type", content: "website" },
    { property: "og:url", content: pageUrl },
    { property: "og:title", content: component.name },
    { property: "og:description", content: description },
    { property: "og:image", content: ogImage },
    { property: "og:image:width", content: "1200" },
    { property: "og:image:height", content: "630" },

    // Twitter
    { name: "twitter:title", content: component.name },
    { name: "twitter:description", content: description },
    { name: "twitter:image", content: ogImage },
    { name: "twitter:image:width", content: "1200" },
    { name: "twitter:image:height", content: "630" },
  ];
};

export async function loader({ params }: LoaderFunctionArgs) {
  const component = components.find((c) => c.name.toLowerCase() === params.name?.toLowerCase());
  if (!component) throw new Response("Not Found", { status: 404 });
  return { component };
}
export default function ComponentDetail() {
  const { component } = useLoaderData<typeof loader>();

  if (!component) {
    return <Navigate to="/components" replace />
  }

  return (
    <div className="animate-in fade-in slide-in-from-bottom-4 space-y-8 duration-500">
      {/* Header */}
      <div>
        <div className="mb-3 flex flex-wrap items-center gap-3">
          <h1 className="text-3xl font-bold tracking-tight text-slate-900 dark:text-slate-50">
            {component.name}
          </h1>
          <span
            className={cn(
              "rounded-full border px-3 py-1 text-xs font-semibold tracking-widest uppercase",
              getCategoryBadgeColor(component.category)
            )}
          >
            {component.category}
          </span>
        </div>
        <p className="text-lg text-slate-600 dark:text-slate-300">{component.description}</p>
      </div>

      {/* Preview */}
      <ComponentPreview component={component} />

      {/* Parameters */}
      {component.parameters && component.parameters.length > 0 && (
        <ParametersTable parameters={component.parameters} />
      )}

      {/* API */}
      <ApiSection apiId={component.apiId} componentName={component.name} />
    </div>
  )
}
