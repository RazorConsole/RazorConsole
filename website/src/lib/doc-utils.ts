// Utilities for cleaning and sanitizing documentation text

/**
 * Clean XML tags and xref from description text.
 * Handles DocFX-style xref tags and extracts readable content.
 */
export function sanitizeDocText(desc?: string): string | undefined {
    if (!desc) return undefined;

    // Remove xref tags and extract the readable content
    let cleaned = desc.replace(
        /<xref[^>]*href="([^"]*)"[^>]*>([^<]*)<\/xref>/g,
        (_, href, text) => {
            // Extract just the type name from the href
            const parts = href.split(".");
            return parts[parts.length - 1] || text || "";
        }
    );

    // Remove any remaining XML tags
    cleaned = cleaned.replace(/<[^>]+>/g, " ");

    // Clean up extra whitespace
    cleaned = cleaned.replace(/\s+/g, " ").trim();

    return cleaned.length > 0 ? cleaned : undefined;
}
