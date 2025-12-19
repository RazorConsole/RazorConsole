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
function parseTypeName(type: string): { displayName: string; baseType: string; isGeneric: boolean } {
    if (!type) return { displayName: "", baseType: "", isGeneric: false };

    // Check if this is a generic type
    const isGeneric = /[{<`]/.test(type);

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
        return { displayName: cleaned, baseType, isGeneric };
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
        const simplifiedArgs = splitGenericArgs(innerTypes).map(arg => {
            const trimmed = arg.trim();
            // Recursively parse nested generic types
            const nested = parseTypeName(trimmed);
            return nested.displayName;
        }).join(", ");

        return { 
            displayName: `${shortTypeName}<${simplifiedArgs}>`, 
            baseType,
            isGeneric: true
        };
    }

    return { displayName: shortTypeName, baseType, isGeneric };
}

// Split generic arguments while respecting nested generics
function splitGenericArgs(argsString: string): string[] {
    const args: string[] = [];
    let depth = 0;
    let current = "";

    for (const char of argsString) {
        if (char === "{" || char === "<") {
            depth++;
            current += char;
        } else if (char === "}" || char === ">") {
            depth--;
            current += char;
        } else if (char === "," && depth === 0) {
            args.push(current.trim());
            current = "";
        } else {
            current += char;
        }
    }

    if (current.trim()) {
        args.push(current.trim());
    }

    return args;
}

// Get the documentation URL for a type (only for non-generic types)
function getTypeDocUrl(baseType: string, isGeneric: boolean): string | null {
    if (!baseType) return null;

    // Don't link generic Microsoft/System types - URLs are complex
    if (isGeneric && (baseType.startsWith("Microsoft.") || baseType.startsWith("System."))) {
        return null;
    }

    // Microsoft/System types -> MS Docs
    if (baseType.startsWith("Microsoft.") || baseType.startsWith("System.")) {
        return `https://learn.microsoft.com/dotnet/api/${baseType.toLowerCase()}`;
    }

    // Spectre.Console types
    if (baseType.startsWith("Spectre.")) {
        return "https://spectreconsole.net/";
    }

    // RazorConsole types -> internal API docs
    if (baseType.startsWith("RazorConsole.")) {
        return `/api/${baseType}`;
    }

    return null;
}

// Render type with links
export function TypeLink({ type }: TypeLinkProps) {
    if (!type) return <span className="text-slate-400">—</span>;

    const { displayName, baseType, isGeneric } = parseTypeName(type);
    const docUrl = getTypeDocUrl(baseType, isGeneric);

    // Microsoft/System types (only link if not generic)
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
