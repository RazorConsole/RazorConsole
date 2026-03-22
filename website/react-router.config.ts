import type { Config } from "@react-router/dev/config";
import { components } from "./src/data/components";
import { apiItems } from "./src/data/api-docs";
import { docTopicIds, releaseNoteIds } from "./src/data/docs-ids";

export default {
    appDirectory: "src",
    ssr: false,
    basename: process.env.VITE_ROUTER_BASENAME || "/",
    async prerender({ getStaticPaths }) {
        // For dynamic routes, that have indexes
        const dynamicPathIndexes = [
            "/docs",
            "/api",
        ]

        const componentPaths = components.map(
            (comp) => `/components/${comp.name.toLowerCase()}`
        );

        const apiPaths = Object.keys(apiItems).map(
            (uid) => `/api/${encodeURIComponent(uid)}`
        );

        const docsPaths = [...docTopicIds, ...releaseNoteIds].map(
            (item) => `/docs/${item.id}`
        );

        return [
            ...getStaticPaths(),
            ...dynamicPathIndexes,
            ...componentPaths,
            ...apiPaths,
            ...docsPaths,
        ];
    },
} satisfies Config;
