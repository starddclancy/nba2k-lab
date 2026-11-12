import test from 'node:test';
import assert from 'node:assert/strict';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';

function button(html: string, label: string): string {
    const match = [...html.matchAll(/<button\b[^>]*>[\s\S]*?<\/button>/g)]
        .find(([entry]) => entry.replace(/<[^>]+>/g, '').replace(/\s/g, '').includes(label.replace(/\s/g, '')));
    assert.ok(match, `Missing button: ${label}`);
    return match[0];
}

test('browse mode blocks game writes while the local build remains editable', async () => {
    const previousWindow = globalThis.window;
    globalThis.window = { location: { search: '?preview', hash: '' } } as Window & typeof globalThis;
    const server = await createServer({
        configFile: false, server: { middlewareMode: true, hmr: false },
        appType: 'custom', esbuild: { jsx: 'automatic' },
    });
    try {
        const { default: RosterPanel } = await server.ssrLoadModule('/src/components/RosterPanel.tsx');
        const { default: BuildStudio } = await server.ssrLoadModule('/src/components/BuildStudio.tsx');
        const roster = renderToStaticMarkup(createElement(RosterPanel));
        assert.match(button(roster, '撤销上一笔'), /\sdisabled(?:=|\s|>)/);
        assert.doesNotMatch(button(roster, '刷新'), /\sdisabled(?:=|\s|>)/);
        const builder = renderToStaticMarkup(createElement(BuildStudio, { view: 'builder' }));
        assert.match(button(builder, '应用到名单球员'), /\sdisabled(?:=|\s|>)/);
        const localName = builder.match(/<input\b[^>]*aria-label="方案名称"[^>]*>/)?.[0];
        assert.ok(localName);
        assert.doesNotMatch(localName, /\sdisabled(?:=|\s|>)/);
    } finally {
        await server.close();
        globalThis.window = previousWindow;
    }
});
