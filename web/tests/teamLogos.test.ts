import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { historicSeasonEnd, resolveTeamLogo } from '../src/teamLogos.ts';
import catalog from '../src/team-logo-catalog.json' with { type: 'json' };
import gameCatalog from '../src/game-logo-catalog.json' with { type: 'json' };

test('historical franchise names retain their own dated branding', () => {
    for (const [label, logoReference, file] of [
        ['Seattle SuperSonics', 'logos/logo024okc_2002.iff', 'game-logo024okc_2002.png'],
        ['New Jersey Nets', 'logos/logo018bkn_1998.iff', 'game-logo018bkn_1998.png'],
        ['New Orleans Hornets', 'logos/logo011no_2003.iff', 'game-logo011no_2003.png'],
        ['Charlotte Bobcats', 'logos/logo031cha_2008.iff', 'game-logo031cha_2008.png'],
        ['Philadelphia 76ers', 'logos/logo000phi_1998.iff', 'game-logo000phi_1998.png'],
    ]) {
        assert.equal(resolveTeamLogo({ label, logoReference })?.file, file);
    }
});

test('same named teams may use distinct logos from their individual game records', () => {
    const label = 'Milwaukee Bucks';
    assert.equal(resolveTeamLogo({ label, logoReference: 'logo001mil_2007.iff' })?.file, 'game-logo001mil_2007.png');
    assert.equal(resolveTeamLogo({ label, logoReference: 'logo001mil_1994.iff' })?.file, 'game-logo001mil_1994.png');
});

test('unknown resources, absent metadata and invalid dates do not invent logos', () => {
    for (const team of [
        { label: 'Seattle SuperSonics', logoReference: 'logos/logo99999.iff' },
        { label: 'New Jersey Nets', logoReference: 'logos/logo018bkn_2026.iff' },
        { label: 'Philadelphia 76ers' },
    ]) {
        assert.equal(resolveTeamLogo(team), null);
    }
});

test('one team switches across every packaged era without a roster entry or a cached name match', () => {
    const eras = gameCatalog.assets.filter((asset) => /^logos\/logo001mil_\d{4}\.iff$/.test(asset.resource));
    assert.ok(eras.length >= 4);
    for (const asset of [...eras, eras[0]]) {
        assert.equal(resolveTeamLogo({ label: 'Milwaukee Bucks', logoReference: asset.resource })?.file,
            asset.file);
    }
    assert.ok(new Set(eras.map((asset) => asset.sha256)).size >= 4);
});

test('blank resource fields use the live historical-year value to select that season logo', () => {
    for (const [label, raw, file, end] of [
        ['Los Angeles Lakers', 65, 'los-angeles-lakers-1960.png', 1965],
        ['Milwaukee Bucks', 71, 'milwaukee-bucks-1968.png', 1971],
        ['New York Knicks', 72, 'new-york-knicks-1964.png', 1972],
        ['Los Angeles Lakers', 101, 'los-angeles-lakers-1976.png', 2001],
        ['Toronto Raptors', 119, 'toronto-raptors-2015.png', 2019],
        ['Oklahoma City Thunder', 112, 'game-logo024okc_2009.png', 2012],
    ] as const) {
        assert.equal(historicSeasonEnd(raw), end);
        assert.equal(resolveTeamLogo({ label, historicYearRaw: raw })?.file, file);
    }
    assert.equal(resolveTeamLogo({ label: 'Unknown team', historicYearRaw: 65 }), null);
});

test('blank-identity special teams resolve only through their explicit game assets', () => {
    for (const [label, file] of [
        ['Rookie', 'game-logo504.png'],
        ['Sophmores', 'game-logo505.png'],
        ['Ballerz Ballerz', 'game-logo619.png'],
        ['Home', 'game-logo_nba_current.png'],
        ['Away', 'game-logo_nba_current.png'],
    ]) {
        assert.equal(resolveTeamLogo({ label })?.file, file);
    }
});

test('resource path normalization accepts base names without crossing namespaces', () => {
    const expected = 'game-logo001mil_1994.png';
    for (const logoReference of ['logo001mil_1994.iff', 'logos/logo001mil_1994',
        'LOGOS\\logo001mil_1994.IFF']) {
        assert.equal(resolveTeamLogo({ label: 'Renamed club', logoReference })?.file, expected);
    }
    assert.equal(resolveTeamLogo({ label: 'Renamed club',
        logoReference: 'custom/logo001mil_1994.iff' }), null);
});

test('an unresolved first logo field does not hide the next live game texture reference', () => {
    assert.equal(resolveTeamLogo({
        label: 'Cleveland Cavaliers',
        logoReference: 'logo_nba_cleveland_cavaliers',
        logoReferences: ['logo_nba_cleveland_cavaliers', 'logos/logo004'],
    })?.file, 'game-logo004.png');
});

test('the game resource namespace resolves dated and numeric logos dynamically', () => {
    assert.equal(gameCatalog.errors.length, 0);
    assert.ok(gameCatalog.enumerated_resources >= 600);
    assert.ok(gameCatalog.assets.length >= 600);
    for (const asset of gameCatalog.assets) {
        const logo = resolveTeamLogo({ label: 'Team name can change', logoReference: asset.resource });
        assert.equal(logo?.file, asset.file, asset.resource);
        const bytes = readFileSync(new URL(`../public/team-logos/${asset.file}`, import.meta.url));
        assert.equal(createHash('sha256').update(bytes).digest('hex'), asset.sha256, asset.file);
        assert.equal(bytes.subarray(1, 4).toString(), 'PNG', asset.file);
        assert.ok(bytes.length > 1000, asset.file);
    }
    assert.equal(resolveTeamLogo({ label: 'Milwaukee Bucks', logoReference: 'LOGOS\\logo001mil_1994.iff' })?.file,
        'game-logo001mil_1994.png');
    assert.equal(resolveTeamLogo({ label: 'Seattle SuperSonics', logoReference: 'logos/logo024okc_2002.iff' })?.file,
        'game-logo024okc_2002.png');
    assert.equal(gameCatalog.teams.length, 142);
    for (const team of gameCatalog.teams) {
        const logo = resolveTeamLogo({
            label: team.name, logoReferences: team.references,
            historicYearRaw: team.historic_year_raw,
        });
        assert.ok(logo, `${team.index} ${team.name} year=${team.historic_year_raw}`);
    }
});

test('resource identity is preserved across renamed teams and opaque game IDs', () => {
    assert.equal(resolveTeamLogo({ label: 'Renamed team', logoReference: 'logos/logo628.iff' })?.file, 'game-logo628.png');
    for (const [reference, file] of [
        ['logos/logo007_secondary.iff', 'game-logo007.png'],
        ['logo_nba_western_conference', 'game-logo030.png'],
        ['logos/logo504.iff', 'game-logo504.png'],
        ['logos/logo619.iff', 'game-logo619.png'],
    ]) {
        assert.equal(resolveTeamLogo({ label: 'Special team', logoReference: reference })?.file, file);
    }
});

test('all bundled catalog assets match their provenance hashes and dated ranges', () => {
    const files = new Set<string>();
    for (const logo of catalog) {
        assert.equal(files.has(logo.file), false, logo.file);
        files.add(logo.file);
        assert.ok(logo.until === null || logo.until > logo.from, logo.file);
        const bytes = readFileSync(new URL(`../public/team-logos/${logo.file}`, import.meta.url));
        assert.equal(createHash('sha256').update(bytes).digest('hex'), logo.sha256, logo.file);
    }
    assert.ok(files.size > 100);
});
