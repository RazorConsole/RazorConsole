import { createServer, resolveConfig } from 'vite';
import type { ComponentInfo } from '../src/types/components/componentInfo.ts';
import type { TopicItem } from '../src/types/docs/topicItem.ts';
import * as fs from 'node:fs';
import * as path from 'node:path';
import pc from 'picocolors';

async function generate() {
    const config = await resolveConfig({}, 'build');
    const SITE_URL = (config.env.VITE_SITE_URL || '').replace(/\/$/, '');
    const BASE_PATH = config.base.replace(/\/$/, '');
    const FULL_BASE_URL = `${SITE_URL}${BASE_PATH}`;

    const DIST_DIR = path.resolve(config.root, config.build.outDir || 'dist');
    const RAW_DIR = path.join(DIST_DIR, 'raw');
    const RAW_DOCS_DIR = path.join(RAW_DIR, 'docs');
    const RAW_COMPS_DIR = path.join(RAW_DIR, 'components');

    console.log(pc.cyan(`[LLMS] Starting documentation generation...`));

    const vite = await createServer({
        server: { middlewareMode: true },
        appType: 'custom'
    });

    try {
        const { components } = await vite.ssrLoadModule('./src/data/components.ts') as { components: ComponentInfo[] };
        const { docTopicIds } = await vite.ssrLoadModule('./src/data/docs-ids.ts') as { docTopicIds: TopicItem[] };

        [RAW_DOCS_DIR, RAW_COMPS_DIR].forEach(dir => {
            if (!fs.existsSync(dir)) {
                fs.mkdirSync(dir, { recursive: true });
                console.log(pc.dim(`[LLMS] Created directory: ${path.relative(config.root, dir)}`));
            }
        });

        const cleanXref = (text: string) => {
            return text.replace(/<xref href=".*?" data-throw-if-not-resolved="false"><\/xref>/g, (match) => {
                const parts = match.match(/href="(.*?)"/);
                return parts ? parts[1].split('.').pop() || parts[1] : '';
            }).replace(/<code>(.*?)<\/code>/g, '`$1`');
        };

        let indexContent = `# RazorConsole\n\n> High-performance Blazor TUI framework, built on top of [Spectre.Console](https://spectreconsole.net).\n\n\n`;
        let fullContent = indexContent;

        // Docs generation
        indexContent += `## Documentation Guides\n\n`;
        for (const doc of docTopicIds) {
            const relativePath = doc.filePath.replace(/^website\//, '');
            const absolutePath = path.resolve(config.root, relativePath);

            if (fs.existsSync(absolutePath)) {
                console.log(pc.dim(`[LLMS] Processing guide: ${doc.title}`));
                const text = fs.readFileSync(absolutePath, 'utf8');
                
                const fileName = `${doc.id}.md`;
                fs.writeFileSync(path.join(RAW_DOCS_DIR, fileName), text);

                indexContent += `- [${doc.title}](${FULL_BASE_URL}/raw/docs/${fileName})\n`;
                fullContent += `\n---\n\n# Document: ${doc.title}\n\n${text}\n`;
            }
        }

        // Components generation
        indexContent += `\n## Components API\n\n`;
        for (const comp of components) {
            console.log(pc.dim(`[LLMS] Processing component: ${comp.name}`));
            let compMd = `# Component: ${comp.name}\n\n${cleanXref(comp.description || '')}\n\n`;

            // Params
            if (comp.parameters && comp.parameters.length > 0) {
                const headers = ['Name', 'Type', 'Default', 'Description'];
                const rows = comp.parameters.map(p => [
                    p.name || '',
                    p.type || '',
                    p.default || '-',
                    cleanXref(p.description || '')
                ]);

                const widths = headers.map((h, i) => Math.max(h.length, ...rows.map(row => row[i].length)));
                const formatRow = (data: string[]) => `| ${data.map((val, i) => val.padEnd(widths[i])).join(' | ')} |\n`;

                compMd += `### Parameters:\n\n`;
                compMd += formatRow(headers);
                compMd += `| ${widths.map(w => '-'.repeat(w)).join(' | ')} |\n`;
                rows.forEach(row => { compMd += formatRow(row); });
                compMd += `\n`;
            }

            // Examples
            if (comp.examples) {
                const examplesArray = Array.isArray(comp.examples) ? comp.examples : [comp.examples];
                for (const exampleFilename of examplesArray) {
                    const examplePath = path.resolve(config.root, '../src/RazorConsole.Website/Components', exampleFilename);
                    if (fs.existsSync(examplePath)) {
                        const exampleCode = fs.readFileSync(examplePath, 'utf8');
                        compMd += `### Usage Example (${exampleFilename}):\n\n\`\`\`\`razor\n${exampleCode}\n\`\`\`\`\n\n`;
                    }
                }
            }

            const compFileName = `${comp.name.toLowerCase()}.md`;
            fs.writeFileSync(path.join(RAW_COMPS_DIR, compFileName), compMd);

            indexContent += `- [${comp.name}](${FULL_BASE_URL}/raw/components/${compFileName}): ${comp.description}\n`;
            fullContent += `\n---\n\n${compMd}`;
        }

        if (fs.existsSync(DIST_DIR)) {
            fs.writeFileSync(path.join(DIST_DIR, 'llms.txt'), indexContent);
            fs.writeFileSync(path.join(DIST_DIR, 'llms-full.txt'), fullContent);
        }

        console.log(pc.green(`[LLMS] Generation finished.`));

    } catch (e) {
        console.error(pc.red(`[LLMS] Generation failed: ${e}`));
    } finally {
        await vite.close();
    }
}

generate();