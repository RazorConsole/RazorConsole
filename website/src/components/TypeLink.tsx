import { Link } from "react-router-dom";
import { ExternalLink } from "lucide-react";

interface TypeLinkProps {
    type?: string;
}

// Parse DocFX type notation and extract readable type name
// Handles formats like:
// - System.Int32
// - Microsoft.AspNetCore.Components.EventCallback{System.Int32}
// - System.Collections.Generic.IReadOnlyList{RazorConsole.Core.Vdom.VdomMutation}
// - RazorConsole.Components.Scrollable`1.ScrollContext{{TItem}}
function parseTypeName(type: string): { displayName: string; baseType: string } {
    if (!type) return { displayName: "", baseType: "" };

    // Extract the base type (before any generic parameters)
    const baseType = type.split(/[{<]/)[0].replace(/`\d+/g, "");

    // Remove generic arity markers like `1, `2
    let cleaned = type.replace(/`\d+/g, "");

    // Replace DocFX generic notation {Type} with <Type>
    // Handle nested generics {{TItem}} -> <TItem>
    cleaned = cleaned.replace(/\{\{([^}]+)\}\}/g, "<$1>");
    cleaned = cleaned.replace(/\{([^}]+)\}/g, "<$1>");

    // Extract the short type name and generic arguments separately
    const genericMatch = cleaned.match(/^([^<]+)(<.+>)?$/);
    if (!genericMatch) {
        return { displayName: cleaned, baseType };
    }

    const [, fullTypeName, genericPart] = genericMatch;
    
    // Get the short name of the main type (last segment before generic)
    const typeNameParts = fullTypeName.split(".");
    const shortTypeName = typeNameParts[typeNameParts.length - 1] ?? fullTypeName;

    // If there's a generic part, simplify the generic arguments
    if (genericPart) {
        // Extract inner type(s) from <...>
        const innerTypes = genericPart.slice(1, -1); // Remove < and >
        
        // Simplify each type argument (get short names)
        const simplifiedArgs = innerTypes.split(",").map(arg => {
            const trimmed = arg.trim();
            // Get the last part after the last dot (but handle nested generics)
            const argParts = trimmed.split(".");
            return argParts[argParts.length - 1] ?? trimmed;
        }).join(", ");

        return { 
            displayName: `${shortTypeName}<${simplifiedArgs}>`, 
            baseType 
        };
    }

    return { displayName: shortTypeName, baseType };
}

// Get the documentation URL for a type
function getTypeDocUrl(baseType: string): string | null {
    if (!baseType) return null;

    // Microsoft/System types -> MS Docs
    if (baseType.startsWith("Microsoft.") || baseType.startsWith("System.")) {
        return `https://learn.microsoft.com/dotnet/api/${baseType
            .toLowerCase()
            .replace(/`\d+/g, "-")}`;
    }

    // Spectre.Console types
    if (baseType.startsWith("Spectre.")) {
        return "https://spectreconsole.net/";
    }

    // RazorConsole types -> internal API docs
    if (baseType.startsWith("RazorConsole.")) {
        const cleanedType = baseType.replace(/`\d+/g, "-1");
        return `/api/${cleanedType}`;
    }

    return null;
}

// Render type with links
export function TypeLink({ type }: TypeLinkProps) {
    if (!type) return <span className="text-slate-400">—</span>;

    const { displayName, baseType } = parseTypeName(type);
    const docUrl = getTypeDocUrl(baseType);

    // Microsoft/System types
    if (baseType.startsWith("Microsoft.") || baseType.startsWith("System.")) {
        return docUrl ? (
            <a
                href={docUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-1 font-mono text-xs text-blue-600 hover:text-blue-700 hover:underline dark:text-blue-400 dark:hover:text-blue-300"
            >
                {displayName}
                <ExternalLink className="h-3 w-3" />
            </a>
        ) : (
            <code className="font-mono text-xs text-blue-600 dark:text-blue-400">
                {displayName}
            </code>
        );
    }

    // Spectre.Console types
    if (baseType.startsWith("Spectre.")) {
        return (
            <a
                href={docUrl ?? "https://spectreconsole.net/"}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-1 font-mono text-xs text-emerald-600 hover:text-emerald-700 hover:underline dark:text-emerald-400 dark:hover:text-emerald-300"
            >
                {displayName}
                <ExternalLink className="h-3 w-3" />
            </a>
        );
    }

    // RazorConsole types -> link to internal API docs
    if (baseType.startsWith("RazorConsole.") && docUrl) {
        return (
            <Link
                to={docUrl}
                className="inline-flex items-center gap-1 font-mono text-xs text-violet-600 hover:text-violet-700 hover:underline dark:text-violet-400 dark:hover:text-violet-300"
            >
                {displayName}
                <ExternalLink className="h-3 w-3" />
            </Link>
        );
    }

    // Default - just display as code
    return (
        <code className="font-mono text-xs text-violet-600 dark:text-violet-400">
            {displayName || type}
        </code>
    );
}
