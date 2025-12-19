import { Settings, Zap, Box, FileCode } from "lucide-react";

// Shared category definitions for parameters and members
export type MemberCategory = "Behavior" | "Appearance" | "Events" | "Common" | "Other";

// Get category icon
export function getCategoryIcon(category: MemberCategory) {
    switch (category) {
        case "Behavior":
            return <Settings className="h-4 w-4" />;
        case "Appearance":
            return <Box className="h-4 w-4" />;
        case "Events":
            return <Zap className="h-4 w-4" />;
        case "Common":
            return <FileCode className="h-4 w-4" />;
        default:
            return null;
    }
}

// Categorize a member/parameter based on its name, type, and description
export function categorizeMember(
    name: string,
    type: string,
    description?: string
): MemberCategory {
    const nameLower = name.toLowerCase();
    const descLower = (description ?? "").toLowerCase();

    // Events
    if (
        nameLower.startsWith("on") ||
        nameLower.includes("callback") ||
        nameLower.includes("event") ||
        type.includes("EventCallback")
    ) {
        return "Events";
    }

    // Appearance
    if (
        nameLower.includes("color") ||
        nameLower.includes("style") ||
        nameLower.includes("class") ||
        nameLower.includes("width") ||
        nameLower.includes("height") ||
        nameLower.includes("size") ||
        nameLower.includes("border") ||
        nameLower.includes("background") ||
        nameLower.includes("foreground") ||
        descLower.includes("appearance") ||
        descLower.includes("visual") ||
        descLower.includes("style")
    ) {
        return "Appearance";
    }

    // Behavior
    if (
        nameLower.includes("enabled") ||
        nameLower.includes("disabled") ||
        nameLower.includes("readonly") ||
        nameLower.includes("visible") ||
        nameLower.includes("focus") ||
        nameLower.includes("selected") ||
        descLower.includes("behavior") ||
        descLower.includes("interact")
    ) {
        return "Behavior";
    }

    // Common
    if (
        nameLower === "childcontent" ||
        nameLower === "id" ||
        nameLower === "class" ||
        nameLower === "style" ||
        nameLower.includes("content") ||
        nameLower.includes("value") ||
        nameLower.includes("text") ||
        nameLower === "title" ||
        nameLower === "label" ||
        nameLower === "header"
    ) {
        return "Common";
    }

    return "Other";
}

// Category order for consistent display
export const CATEGORY_ORDER: MemberCategory[] = [
    "Behavior",
    "Appearance",
    "Events",
    "Common",
    "Other",
];
