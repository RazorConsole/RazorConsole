import { createServer, resolveConfig } from 'vite';
import { JSDOM } from 'jsdom';
import { registerFont, createCanvas } from 'canvas';
import fs from 'node:fs';
import path from 'node:path';
import satori from 'satori';
import { Resvg } from '@resvg/resvg-js';
import pc from 'picocolors';
import xtermPkg from '@xterm/headless';
import { prepareWithSegments, layoutWithLines } from '@chenglou/pretext';
const { Terminal } = xtermPkg;

import type { ComponentInfo } from '../src/types/components/componentInfo.ts';

const ANSI_HEX: Record<number, string> = {
    0: "#000000", 1: "#cd3131", 2: "#0dbc79", 3: "#e5e510",
    4: "#2472c8", 5: "#bc3fbc", 6: "#11a8cd", 7: "#e5e5e5",
    8: "#666666", 9: "#cd3131", 10: "#14ce96", 11: "#f5f543",
    12: "#3b8eea", 13: "#d670d6", 14: "#29b8db", 15: "#a8a8a8",
};

function get256Color(index: number): string {
    if (index < 16) return ANSI_HEX[index] || '#cccccc';
    if (index < 232) {
        const i = index - 16;
        const r = Math.floor(i / 36) * 51;
        const g = Math.floor((i % 36) / 6) * 51;
        const b = (i % 6) * 51;
        return `#${r.toString(16).padStart(2, '0')}${g.toString(16).padStart(2, '0')}${b.toString(16).padStart(2, '0')}`;
    }
    const grayscale = (index - 232) * 10 + 8;
    const hex = grayscale.toString(16).padStart(2, '0');
    return `#${hex}${hex}${hex}`;
}

function resolveColor(colorValue: number, isForeground: boolean): string {
    if (colorValue === -1 || colorValue >= 0x01000000 && (colorValue & 0xFFFFFF) === 0) {
        return isForeground ? "#cccccc" : "transparent";
    }

    const value = colorValue & 0xFFFFFF;
    if (value > 255) {
        const r = (value >> 16) & 0xFF;
        const g = (value >> 8) & 0xFF;
        const b = value & 0xFF;
        return `#${r.toString(16).padStart(2, '0')}${g.toString(16).padStart(2, '0')}${b.toString(16).padStart(2, '0')}`;
    }

    return get256Color(value);
}

async function generateOgImages() {
    const config = await resolveConfig({}, 'build');
    const DIST_DIR = path.resolve(config.root, config.build.outDir || 'dist');
    const OG_DIR = path.join(DIST_DIR, 'og');
    const ASSETS_DIR = path.resolve(config.root, 'src/assets/fonts');

    const FONT_PATH = path.join(ASSETS_DIR, 'CascadiaCode.ttf');
    const FONT_PATH_BOLD = path.join(ASSETS_DIR, 'CascadiaCode-Bold.ttf');
    const FONT_PATH_ITALIC = path.join(ASSETS_DIR, 'CascadiaCode-Italic.ttf');

    registerFont(FONT_PATH, { family: 'Cascadia Code', weight: 'normal', style: 'normal' });
    registerFont(FONT_PATH_BOLD, { family: 'Cascadia Code', weight: 'bold', style: 'normal' });
    registerFont(FONT_PATH_ITALIC, { family: 'Cascadia Code', weight: 'normal', style: 'italic' });

    if (!fs.existsSync(OG_DIR)) fs.mkdirSync(OG_DIR, { recursive: true });
    
    const fontData = fs.readFileSync(FONT_PATH);
    const fontBoldData = fs.readFileSync(FONT_PATH_BOLD);
    const fontItalicData = fs.readFileSync(FONT_PATH_ITALIC);

    const termCols = 80;
    const termRows = 24;

    const dom = new JSDOM('<!DOCTYPE html><html><body></body></html>', {
        url: "http://localhost",
        pretendToBeVisual: true 
    });
    
    global.window = dom.window as any;
    if (typeof global.OffscreenCanvas === 'undefined') {
        // @ts-ignore
        global.OffscreenCanvas = class {
            constructor(width: number, height: number) {
                return createCanvas(width, height);
            }
        };
    }

    const nativeFetch = global.fetch;
    global.fetch = async (input: any, init?: any) => {
        const url = typeof input === 'string' ? input : input.url;
        if (!url.startsWith('http') && (url.includes('.wasm') || url.includes('.dat'))) {
            const fileName = url.split('/').pop()?.split('?')[0];
            const artifactPath = path.resolve(config.root, '../artifacts/publish/RazorConsole.Website/release/wwwroot/_framework', fileName!);
            if (fs.existsSync(artifactPath)) {
                return new Response(fs.readFileSync(artifactPath), {
                    status: 200,
                    headers: { 'Content-Type': url.endsWith('.wasm') ? 'application/wasm' : 'application/octet-stream' }
                });
            }
        }
        return nativeFetch(input, init);
    };

    const vite = await createServer({
        server: { middlewareMode: true },
        logLevel: 'error',
        ssr: { external: ['razor-console'] },
        appType: 'custom'
    });

    try {
        const { createRuntimeAndGetExports } = await vite.ssrLoadModule('razor-console');
        const wasmExports = await createRuntimeAndGetExports();
        const { components } = await vite.ssrLoadModule('./src/data/components.ts') as { components: ComponentInfo[] };

        const capturedAnsi: Record<string, string> = {};
        (global.window as any).razorConsoleTerminal = {
            write: (id: string, text: string) => { capturedAnsi[id] = (capturedAnsi[id] || '') + text; },
            init: async () => { }, clear: () => { }, dispose: () => { }, attachKeyListener: () => { }
        };

        for (const comp of components) {
            console.log(pc.cyan(`[OG] Processing: ${comp.name}`));

            const term = new Terminal({
                cols: termCols,
                rows: termRows,
                allowProposedApi: true,
                convertEol: true
            });

            capturedAnsi[comp.name] = '';
            await wasmExports.Registry.RegisterComponent(comp.name, termCols, termRows);
            if (wasmExports.Registry.HandleResize) {
                await wasmExports.Registry.HandleResize(comp.name, termCols, termRows);
            }

            await new Promise(resolve => setTimeout(resolve, 600));

            const ansiData = capturedAnsi[comp.name] || '';
            console.log(pc.dim(`      Captured ${ansiData.length} ANSI bytes`));

            await new Promise<void>((resolve) => {
                term.write(ansiData, () => setTimeout(resolve, 50));
            });

            const svg = await satori(
                <div style={{
                    height: '100%', width: '100%', display: 'flex', flexDirection: 'column',
                    alignItems: 'center', justifyContent: 'center', backgroundColor: '#0f172a',
                    padding: '40px',
                    backgroundImage: 'linear-gradient(to bottom right, #020618, #170E37)',
                }}>
                    <div style={{
                        display: 'flex', flexDirection: 'column', width: '1000px', height: '540px',
                        backgroundColor: '#1e1e1e', borderRadius: '16px', overflow: 'hidden',
                        boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.5)'
                    }}>
                        <div style={{ display: 'flex', height: '44px', backgroundColor: '#2d2d2d', alignItems: 'center', padding: '0 18px' }}>
                            <div style={{ display: 'flex', gap: '6px' }}>
                                <div style={{ width: '12px', height: '12px', borderRadius: '6px', backgroundColor: '#ff5f56' }} />
                                <div style={{ width: '12px', height: '12px', borderRadius: '6px', backgroundColor: '#ffbd2e' }} />
                                <div style={{ width: '12px', height: '12px', borderRadius: '6px', backgroundColor: '#27c93f' }} />
                            </div>
                            <div style={{ display: 'flex', flex: 1, color: '#94a3b8', fontSize: '14px', justifyContent: 'center', marginRight: '60px', fontFamily: 'Cascadia Code' }}>
                                RazorConsole // {comp.name}.razor
                            </div>
                        </div>

                        <div style={{
                            padding: '30px', display: 'flex', flexDirection: 'column',
                            fontSize: '20px', fontFamily: 'Cascadia Code',
                            backgroundColor: '#1e1e1e', height: '100%',
                            gap: 0
                        }}>
                            {renderTerminalToJSX(term)}
                        </div>
                    </div>
                </div>,
                {
                    width: 1200,
                    height: 630,
                    fonts: [
                        { name: 'Cascadia Code', data: fontData, weight: 400, style: 'normal' },
                        { name: 'Cascadia Code', data: fontBoldData, weight: 700, style: 'normal' },
                        { name: 'Cascadia Code', data: fontItalicData, weight: 400, style: 'italic' },
                    ]
                }
            );

            const resvg = new Resvg(svg, { background: '#0f172a' });
            const componentOgPath = path.join(OG_DIR, `${comp.name.toLowerCase()}.png`);
            console.log(pc.cyan(`[OG] Saving snapshot of ${comp.name}...`));
            fs.writeFileSync(componentOgPath, resvg.render().asPng());
            console.log(pc.green(`[OG] Saved snapshot of ${comp.name} at ${componentOgPath}`));
        }

        console.log(pc.green(`[OG] All snapshots saved to ${OG_DIR}.`));

    } catch (e) {
        console.error(pc.red(`[OG] Error: ${e}`));
    } finally {
        await vite.close();
        process.exit(0); // Save exit after all snapshots are rendered
    }
}

const FONT_SPEC_BASE = '20px "Cascadia Code"';

function renderTerminalToJSX(term: any) {
    const rows = [];
    const buffer = term.buffer.active;

    console.log(pc.gray("--- TERMINAL SNAPSHOT START ---"));

    for (let y = 0; y < term.rows; y++) {
        const line = buffer.getLine(y);
        if (!line) continue;

        const rawLineText = line.translateToString(false);
        if (rawLineText.trim().length > 0) {
            console.log(pc.green(`[Row ${y}]: `) + rawLineText);
        }

        const rowSpans = [];
        let currentSegment = '';
        let lastFg = -1;
        let lastBg = -1;
        let lastBold = false;
        let lastItalic = false;

        for (let x = 0; x < term.cols; x++) {
            const cell = line.getCell(x);
            if (!cell) continue;

            const fg = cell.getFgColor();
            const bg = cell.getBgColor();
            const bold = !!cell.isBold();
            const italic = !!cell.isItalic();
            const char = cell.getChars() || ' ';

            if (fg === lastFg && bg === lastBg && bold === lastBold && italic === lastItalic) {
                currentSegment += char;
            } else {
                if (currentSegment) {
                    rowSpans.push(createSpan(currentSegment, lastFg, lastBg, lastBold, lastItalic, y, x));
                }
                currentSegment = char;
                lastFg = fg;
                lastBg = bg;
                lastBold = bold;
                lastItalic = italic;
            }
        }

        if (currentSegment) {
            rowSpans.push(createSpan(currentSegment, lastFg, lastBg, lastBold, lastItalic, y, term.cols));
        }

        rows.push(
            <div key={y} style={{
                display: 'flex',
                flexDirection: 'row',
                height: '22px',
                width: 'auto',
                backgroundColor: '#1e1e1e',
                margin: 0,
                padding: 0,
                alignItems: 'stretch',
                justifyContent: 'flex-start',
                overflow: 'hidden'
            }}>
                {rowSpans}
            </div>
        );
    }
    console.log(pc.gray("--- TERMINAL SNAPSHOT END ---"));
    return rows;
}

function createSpan(text: string, fg: number, bg: number, isBold: boolean, isItalic: boolean, y: number, x: number) {
    const fgColor = resolveColor(fg, true);
    const bgColor = resolveColor(bg, false);

    const fontSpec = `${isItalic ? 'italic ' : ''}${isBold ? 'bold ' : ''}${FONT_SPEC_BASE}`;

    const prepared = prepareWithSegments(text, fontSpec, { whiteSpace: 'pre-wrap' });
    const { lines } = layoutWithLines(prepared, 2000, 22);

    const exactWidth = (lines[0]?.width > 0) ? lines[0].width : text.length * 12;

    return (
        <span key={`${y}-${x}`} style={{
            color: fgColor,
            backgroundColor: bgColor,
            display: 'block',
            width: `${exactWidth}px`,
            height: '22px',
            lineHeight: '22px',
            fontFamily: 'Cascadia Code',
            fontSize: '20px',
            fontWeight: isBold ? 'bold' : 'normal',
            fontStyle: isItalic ? 'italic' : 'normal',
            whiteSpace: 'pre',
            margin: 0,
            padding: 0,
            flexShrink: 0,
            flexGrow: 0,
            fontVariantLigatures: 'none',
            fontFeatureSettings: '"liga" 0, "calt" 0',
            textAlign: 'left'
        }}>
            {text}
        </span>
    );
}

generateOgImages();