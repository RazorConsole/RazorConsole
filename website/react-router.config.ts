import type {Config} from "@react-router/dev/config";

export default {
    appDirectory: "src",
    ssr: false,
    async prerender() {
        return [
            "/",
            "/quick-start",
            "/docs",
            "/components",
            "/showcase",
            "/collaborators",
        ];
    },
} satisfies Config;
