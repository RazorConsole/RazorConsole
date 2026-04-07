import { createServer, resolveConfig } from 'vite';
import * as fs from 'node:fs';
import * as path from 'node:path';
import pc from 'picocolors';
import type { ComponentInfo } from '@/types/components/componentInfo';
import type { TopicItem } from '@/types/docs/topicItem';

async function generateSitemap() {
    const config = await resolveConfig({}, 'build');
    const SITE_URL = (config.env.VITE_SITE_URL || '').replace(/\/$/, '');
    const BASE_PATH = config.base.replace(/\/$/, '');
    const FULL_BASE_URL = `${SITE_URL}${BASE_PATH}`;
    const DIST_DIR = path.resolve(config.root, config.build.outDir || 'dist');
    
    const componentPriority = '0.9';
    const componentFreq = 'weekly';
    const docsPriority = '0.8';
    const docsFreq = 'weekly';
    const apiPriority = '0.5';
    const apiFreq = 'monthly';

    console.log(pc.cyan(`[SITEMAP] Initializing sitemap generation for ${FULL_BASE_URL}`));

    const vite = await createServer({
        server: { middlewareMode: true },
        appType: 'custom'
    });

    try {
        const { components } = await vite.ssrLoadModule('./src/data/components.ts') as { components: ComponentInfo[] };
        const { docTopicIds, releaseNoteIds } = await vite.ssrLoadModule('./src/data/docs-ids.ts') as { docTopicIds: TopicItem[], releaseNoteIds: TopicItem[] };
        const { apiItems } = await vite.ssrLoadModule('./src/data/api-docs.ts') as { apiItems: Record<string, any> };

        const lastMod = new Date().toISOString().split('T')[0];
        let xml = `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n`;
        let urlCount = 0;

        const addUrl = (route: string, priority: string, freq: string) => {
            const url = `${FULL_BASE_URL}${route}`.replace(/([^:]\/)\/+/g, "$1");
            xml += `  <url>\n    <loc>${url}</loc>\n    <lastmod>${lastMod}</lastmod>\n    <changefreq>${freq}</changefreq>\n    <priority>${priority}</priority>\n  </url>\n`;
            console.log(pc.dim(`[SITEMAP] Added URL [Priority:${priority}]: ${route}`));
            urlCount++;
        };

        // Root
        addUrl('/', '1.0', 'daily');

        // Components
        components.forEach(c => {
            addUrl(`/components/${c.name.toLowerCase()}`, componentPriority, componentFreq);
        });

        // Documentation
        addUrl('/docs', docsPriority, docsFreq);
        docTopicIds.forEach(d => {
            addUrl(`/docs/${d.id}`, docsPriority, docsFreq);
        });
        (releaseNoteIds || []).forEach(r => {
            addUrl(`/docs/${r.id}`, docsPriority, docsFreq);
        });

        // API Reference
        addUrl('/api', apiPriority, apiFreq);
        Object.keys(apiItems).forEach(uid => {
            addUrl(`/api/${encodeURIComponent(uid)}`, apiPriority, apiFreq);
        });

        xml += `</urlset>`;

        if (!fs.existsSync(DIST_DIR)) fs.mkdirSync(DIST_DIR, { recursive: true });
        
        fs.writeFileSync(path.join(DIST_DIR, 'sitemap.xml'), xml);
        console.log(pc.green(`[SITEMAP] Successfully generated sitemap.xml with ${urlCount} URLs`));
    }
    catch(e){
        console.error(pc.red(`[SITEMAP] Generation failed: ${e}`));
    }
    finally {
        await vite.close();
    }
}

generateSitemap();