// UI smoke test of the sample: drives the built Angular application in a headless Chromium browser (Edge or Chrome,
// through the DevTools protocol) against running hosts, with the demo data of the Backend (Albert / pass, the weak
// actor Operator). The passkeys use a virtual authenticator of the DevTools protocol (WebAuthn domain): Albert adds a
// passkey in the "Sign-in methods" tab of his profile after his Basic login, renames it, logs in with it, then deletes it.
//
//   node ui-smoke/ui-smoke.mjs [--url http://localhost:5300] [--out <folder>] [--oidc <SchemeSuffix>] [--critical-wait <seconds>] [--browser <exe>]
//
// --oidc checks that the login page has the button of this OpenID Connect scheme (the flow itself is covered by the
// HTTP tests). --critical-wait tests the end of the Critical level of the Basic login: the hosts must have a short
// "AuthCenter:CriticalLifetimes:Basic", the smoke waits this number of seconds at most, then Albert logs in again from
// the passkey registration button of his profile, in a popup. The browser is --browser, else the CK_BROWSER environment variable, else Edge or Chrome at their usual
// places. Exit code: 0 on success, 1 on failure, 2 when no browser is found. A screenshot is taken at each step (and on
// failure) in --out (default: ui-smoke/screenshots).
import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const args = Object.fromEntries( process.argv.slice( 2 ).reduce( ( a, v, i, all ) => v.startsWith( '--' ) ? [...a, [v.substring( 2 ), all[i + 1]]] : a, [] ) );
const base = ( args.url ?? 'http://localhost:5300' ).replace( /\/$/, '' );
const out = args.out ?? join( dirname( fileURLToPath( import.meta.url ) ), 'screenshots' );
const browserPath = args.browser ?? process.env.CK_BROWSER ?? [
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  '/usr/bin/microsoft-edge',
  '/usr/bin/google-chrome',
  '/usr/bin/chromium',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'
].find( p => existsSync( p ) );
if( !browserPath || !existsSync( browserPath ) ) {
  console.log( 'No browser found (use --browser or CK_BROWSER).' );
  process.exit( 2 );
}
mkdirSync( out, { recursive: true } );

const sleep = ms => new Promise( r => setTimeout( r, ms ) );
const freePort = () => new Promise( r => { const s = createServer().listen( 0, '127.0.0.1', () => { const p = s.address().port; s.close( () => r( p ) ); } ); } );

// The browser, on its own profile.
const port = await freePort();
const profile = mkdtempSync( join( tmpdir(), 'ck-ui-smoke-' ) );
const browser = spawn( browserPath, ['--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`,
                                     '--no-first-run', '--no-default-browser-check', '--window-size=1400,900', 'about:blank'],
                       { stdio: 'ignore' } );
let version;
for( let i = 0; i < 100 && !version; ++i ) {
  try { version = await ( await fetch( `http://127.0.0.1:${port}/json/version` ) ).json(); } catch { await sleep( 100 ); }
}
if( !version ) {
  console.log( `The browser '${browserPath}' did not start.` );
  browser.kill();
  process.exit( 1 );
}

// The DevTools protocol on a page of the browser (a popup has its own page).
const connect = async webSocketDebuggerUrl => {
  const ws = new WebSocket( webSocketDebuggerUrl );
  await new Promise( r => ws.addEventListener( 'open', r ) );
  let id = 0;
  const pending = new Map();
  ws.addEventListener( 'message', e => {
    const m = JSON.parse( e.data );
    if( m.id && pending.has( m.id ) ) { pending.get( m.id )( m ); pending.delete( m.id ); }
  } );
  const send = ( method, params = {} ) => new Promise( r => { const i = ++id; pending.set( i, r ); ws.send( JSON.stringify( { id: i, method, params } ) ); } );
  // A user gesture: a click can open a popup.
  const evaluate = async expr => ( await send( 'Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true, userGesture: true } ) ).result?.result?.value;
  return { send, evaluate, close: () => ws.close() };
};

class SmokeFailure extends Error { }
const check = ( condition, message ) => { if( !condition ) throw new SmokeFailure( message ); };
// The helpers on a page.
const helpers = evaluate => {
  const path = () => evaluate( 'location.pathname' );
  const exists = selector => evaluate( `!!document.querySelector(${JSON.stringify( selector )})` );
  const text = async selector => ( await evaluate( `document.querySelector(${JSON.stringify( selector )})?.innerText` ) ?? '' ).replace( /\s+/g, ' ' ).trim();
  const waitFor = async ( selector, ms = 15000 ) => {
    for( let t = 0; t < ms; t += 100 ) {
      if( await exists( selector ) ) return;
      await sleep( 100 );
    }
    throw new SmokeFailure( `'${selector}' did not appear (at ${await path()}).` );
  };
  // The Angular reactive forms listen to the input event.
  const fill = ( selector, value ) => evaluate( `(() => { const e = document.querySelector(${JSON.stringify( selector )}); e.value = ${JSON.stringify( value )}; e.dispatchEvent( new Event( 'input', { bubbles: true } ) ); })()` );
  const click = async selector => {
    await waitFor( selector );
    await evaluate( `document.querySelector(${JSON.stringify( selector )}).click()` );
  };
  return { path, exists, text, waitFor, fill, click };
};
// The pages of the browser whose URL contains a text (a popup).
const pagesWith = async urlPart => ( await ( await fetch( `http://127.0.0.1:${port}/json/list` ) ).json() ).filter( t => t.type === 'page' && t.url.includes( urlPart ) );

const target = await ( await fetch( `http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' } ) ).json();
const { send, evaluate, close } = await connect( target.webSocketDebuggerUrl );
const { path, exists, text, waitFor, fill, click } = helpers( evaluate );
let step = 'start';
const shot = async name => {
  const r = await send( 'Page.captureScreenshot', { format: 'png' } );
  if( r.result ) writeFileSync( join( out, `${name}.png` ), Buffer.from( r.result.data, 'base64' ) );
};
const navigate = async url => { await send( 'Page.navigate', { url: base + url } ); await sleep( 300 ); };
const run = async ( name, action ) => {
  step = name;
  await action();
  await sleep( 300 );
  await shot( name );
  console.log( `ok   ${name}` );
};
// The "Sign-in methods" tab of the profile page.
const signInMethods = async () => {
  await navigate( '/profile' );
  await waitFor( 'ck-user-profile-page .ant-tabs-tab' );
  await evaluate( `[...document.querySelectorAll( 'ck-user-profile-page .ant-tabs-tab' )].find( t => t.innerText.includes( 'Sign-in methods' ) )?.querySelector( '.ant-tabs-tab-btn' )?.click()` );
  await waitFor( 'ck-sign-in-methods ck-passkey-list' );
};
// Clicks the element of a list item (nz-list-item) that contains a text: false when there is no such item.
const clickInItem = ( list, itemText, selector ) => evaluate( `(() => {
  const i = [...document.querySelectorAll( ${JSON.stringify( list + ' nz-list-item' )} )].find( e => e.innerText.includes( ${JSON.stringify( itemText )} ) );
  i?.querySelector( ${JSON.stringify( selector )} )?.click();
  return !!i;
})()` );
const waitForText = async ( selector, expected, present = true, ms = 15000 ) => {
  for( let t = 0; t < ms; t += 100 ) {
    if( ( await text( selector ) ).includes( expected ) === present ) return;
    await sleep( 100 );
  }
  throw new SmokeFailure( `'${selector}' ${present ? 'does not contain' : 'still contains'} '${expected}'.` );
};
const passkeyName = `Smoke key ${Date.now()}`;
const renamedPasskey = `${passkeyName} (renamed)`;
const helloResult = async () => {
  await click( 'sample-home button' );
  await waitFor( 'sample-home nz-alert' );
  return text( 'sample-home nz-alert' );
};

let failed = false;
let passkeyCount;
try {
  await send( 'Page.enable' );
  await send( 'Emulation.setDeviceMetricsOverride', { width: 1400, height: 900, deviceScaleFactor: 1, mobile: false } );
  // A platform authenticator that stores discoverable credentials and verifies the user (the Backend requires both).
  await send( 'WebAuthn.enable', { enableUI: false } );
  const authenticatorId = ( await send( 'WebAuthn.addVirtualAuthenticator', {
    options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true }
  } ) ).result?.authenticatorId;
  check( authenticatorId, 'The browser must support the WebAuthn virtual authenticators.' );
  passkeyCount = async () => ( await send( 'WebAuthn.getCredentials', { authenticatorId } ) ).result?.credentials?.length ?? 0;

  await run( '1-anonymous-public-home', async () => {
    await navigate( '/' );
    await waitFor( 'sample-public-home nz-result' );
    check( !await exists( 'sample-home' ), 'The anonymous must not see the hello card.' );
  } );

  await run( '2-login-page', async () => {
    await click( 'sample-public-home button' );
    await waitFor( 'ck-weak-login-form input' );
    check( await path() === '/auth', 'The login page is /auth.' );
    check( await exists( 'ck-basic-login-form input[formcontrolname=password]' ), 'The Basic form is on the login page.' );
    if( args.oidc ) {
      await waitFor( 'button.ck-login-provider-btn' );
      check( ( await text( '.ck-login-providers' ) ).includes( args.oidc ), `The '${args.oidc}' OpenID Connect button is on the login page.` );
    }
  } );

  await run( '3-wrong-password', async () => {
    await fill( 'ck-basic-login-form input[formcontrolname=userName]', 'Albert' );
    await fill( 'ck-basic-login-form input[formcontrolname=password]', 'not-the-password' );
    await click( 'ck-basic-login-form button.login-form-button' );
    await sleep( 1500 );
    check( await path() === '/auth', 'A wrong password stays on the login page.' );
  } );

  await run( '4-weak-login', async () => {
    await fill( 'ck-weak-login-form input', 'Operator' );
    await click( 'ck-weak-login-form button' );
    await waitFor( 'sample-public-home sample-home' );
    const card = await text( 'sample-home' );
    check( card.includes( 'Operator' ) && card.includes( 'Unsafe' ), `The weak actor is on the public page at the Unsafe level (${card}).` );
    const hello = await helloResult();
    check( hello.includes( 'Normal authentication level required' ), `The hello of a weak actor is refused (${hello}).` );
  } );

  await run( '5-weak-sign-out', async () => {
    await click( '.sample-public-home-actions button:nth-child(2)' );
    await waitFor( 'ck-logout' );
  } );

  await run( '6-basic-login', async () => {
    await navigate( '/auth' );
    await fill( 'ck-basic-login-form input[formcontrolname=userName]', 'Albert' );
    await fill( 'ck-basic-login-form input[formcontrolname=password]', 'pass' );
    await click( 'ck-basic-login-form button.login-form-button' );
    await waitFor( 'ck-backoffice-layout sample-home' );
    check( await path() === '/', 'Albert is redirected to the private home.' );
    const hello = await helloResult();
    check( hello.includes( 'Hello Albert' ), `Albert's hello succeeds (${hello}).` );
  } );

  await run( '7-sign-in-methods', async () => {
    await signInMethods();
    const methods = await text( 'ck-sign-in-methods' );
    check( methods.includes( 'Password' ), `The sign-in methods list the password (${methods}).` );
    check( await exists( 'ck-password-section ck-change-password-form' ), 'Albert has a password: the section proposes to change it.' );
    if( args.oidc ) {
      await waitForText( 'ck-oidc-identities', args.oidc );
    }
  } );

  await run( '7-passkey-registration', async () => {
    // Enabled at the Critical level only: right after a login. The passkey is named at its creation.
    await fill( '.ck-passkey-new-name', passkeyName );
    await click( 'ck-passkey-list ck-passkey-registration-button button:not([disabled])' );
    for( let t = 0; t < 15000 && await passkeyCount() === 0; t += 100 ) await sleep( 100 );
    check( await passkeyCount() === 1, 'The authenticator holds the new passkey.' );
    await waitForText( 'ck-passkey-list', passkeyName );
  } );

  await run( '7-passkey-rename', async () => {
    check( await clickInItem( 'ck-passkey-list', passkeyName, '.ck-passkey-rename' ), 'The new passkey is listed.' );
    await waitFor( '.ck-passkey-name-input' );
    await fill( '.ck-passkey-name-input', renamedPasskey );
    await click( 'ck-passkey-list nz-list-item button.ant-btn-primary' );
    await waitForText( 'ck-passkey-list', renamedPasskey );
  } );

  await run( '8-architecture', async () => {
    await navigate( '/architecture' );
    // The buttons of the first card: one per party (the front, then its AuthCenter: the Backend), once the topology is read.
    const atBackend = 'sample-architecture nz-card:first-of-type button:nth-of-type(2)';
    await waitFor( atBackend );
    check( ( await text( 'sample-architecture' ) ).includes( 'Connected' ), 'The topology shows the connected Backend.' );
    await click( atBackend );
    await waitFor( 'sample-architecture nz-alert' );
    const answer = await text( 'sample-architecture nz-alert' );
    check( answer.includes( 'Backend' ) && answer.includes( 'Albert' ) && answer.includes( 'Critical' ),
           `Albert's proof is verified by the Backend (the AuthCenter): Critical there (${answer}).` );
    // The console: the first predefined request is refused.
    await click( '.sample-architecture-requests a' );
    await click( 'sample-architecture button.ant-btn-primary' );
    await waitFor( '.sample-architecture-response' );
    const response = await text( '.sample-architecture-response' );
    check( response.includes( 'not accepted from HTTP' ), `The [Internal] operation is refused (${response}).` );
  } );

  await run( '9-user-info-box', async () => {
    await click( 'ck-user-info-box nz-avatar' );
    await waitFor( '.ck-user-info-box-username' );
    check( await text( '.ck-user-info-box-username' ) === 'Albert', 'The user info box shows Albert.' );
  } );

  await run( '10-reload', async () => {
    await navigate( '/' );
    await waitFor( 'ck-backoffice-layout sample-home' );
  } );

  if( args['critical-wait'] ) {
    await run( '11-critical-login-again', async () => {
      // The Critical level of the Basic login ends: the passkey registration offers to log in again, in a popup.
      await signInMethods();
      await waitFor( '.ck-passkey-registration-login', Number( args['critical-wait'] ) * 1000 );
      await click( '.ck-passkey-registration-login' );
      let found = [];
      for( let t = 0; t < 15000 && found.length === 0; t += 100 ) {
        found = await pagesWith( '/auth?critical' );
        if( found.length === 0 ) await sleep( 100 );
      }
      check( found.length === 1, 'The login page is in a popup.' );
      const popup = await connect( found[0].webSocketDebuggerUrl );
      try {
        const p = helpers( popup.evaluate );
        await p.waitFor( 'ck-basic-login-form input[formcontrolname=password]' );
        check( ( await p.text( '.ck-login-welcome' ) ).includes( "Confirm it's you" ), 'The login page asks for a new login.' );
        check( !await p.exists( 'ck-weak-login-form' ), 'Only the schemes of Albert that can elevate him are proposed.' );
        await p.fill( 'ck-basic-login-form input[formcontrolname=userName]', 'Albert' );
        await p.fill( 'ck-basic-login-form input[formcontrolname=password]', 'pass' );
        await p.click( 'ck-basic-login-form button.login-form-button' );
      }
      finally {
        popup.close();
      }
      // The profile sees the Critical level (the session is shared) and closes the popup: it has stayed on its tab.
      await waitFor( 'ck-passkey-registration-button button:not([disabled])' );
      check( await path() === '/profile', 'Albert has stayed on his profile.' );
      check( ( await text( 'ck-user-profile-page .ant-tabs-tab-active' ) ).includes( 'Sign-in methods' ), 'The profile is still on its "Sign-in methods" tab.' );
      for( let t = 0; t < 5000 && ( await pagesWith( '/auth?critical' ) ).length > 0; t += 100 ) await sleep( 100 );
      check( ( await pagesWith( '/auth?critical' ) ).length === 0, 'The popup is closed.' );
    } );
  }

  const signOut = async () => {
    await click( 'ck-user-info-box nz-avatar' );
    await click( '.ck-user-info-box-menu-item.logout' );
    await waitFor( 'ck-logout' );
    await navigate( '/' );
    await waitFor( 'sample-public-home nz-result' );
  };

  await run( '12-sign-out', signOut );

  await run( '13-passkey-login', async () => {
    await navigate( '/auth' );
    await click( 'ck-passkey-login-button button' );
    await waitFor( 'ck-backoffice-layout sample-home' );
    check( await path() === '/', 'Albert is redirected to the private home.' );
    const card = await text( 'sample-home' );
    check( card.includes( 'Albert' ) && card.includes( 'Critical' ), `Albert is logged in with his passkey at the Critical level (${card}).` );
  } );

  await run( '14-passkey-delete', async () => {
    // At the Critical level of the passkey login: the passkey is deleted (Albert keeps his password).
    await signInMethods();
    // The list is read when the tab opens.
    await waitForText( 'ck-passkey-list', renamedPasskey );
    check( await clickInItem( 'ck-passkey-list', renamedPasskey, '.ck-passkey-delete' ), 'The renamed passkey is listed.' );
    await click( '.ant-popover button.ant-btn-primary' );
    await waitForText( 'ck-passkey-list', renamedPasskey, false );
  } );

  await run( '15-passkey-sign-out', signOut );
  console.log( 'UI smoke test passed.' );
}
catch( e ) {
  failed = true;
  console.log( `FAIL ${step}: ${e instanceof SmokeFailure ? e.message : e?.stack ?? e}` );
  await shot( `failure-${step}` ).catch( () => { } );
}
finally {
  close();
  // Closes the browser (and its child processes) cleanly, then its profile.
  try {
    const b = new WebSocket( version.webSocketDebuggerUrl );
    await new Promise( r => b.addEventListener( 'open', r ) );
    b.send( JSON.stringify( { id: 1, method: 'Browser.close' } ) );
  } catch { browser.kill(); }
  for( let i = 0; i < 20; ++i ) {
    try { rmSync( profile, { recursive: true, force: true } ); break; } catch { await sleep( 250 ); }
  }
}
process.exit( failed ? 1 : 0 );
