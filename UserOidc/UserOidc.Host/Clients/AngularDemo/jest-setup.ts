//-AllowAutoUpdate

// Makes this file a module (see https://stackoverflow.com/questions/57132428/augmentations-for-the-global-scope-can-only-be-directly-nested-in-external-modul).
export {} 

// Augments the global with our CKTypeScriptEnv. See https://stackoverflow.com/questions/59459312/using-globalthis-in-typescript
declare global { var CKTypeScriptEnv: { [key: string]: string}; }

// This fixes a bug in testEnvionment: 'jsdom'. See https://stackoverflow.com/questions/68468203/why-am-i-getting-textencoder-is-not-defined-in-jest
const { TextEncoder, TextDecoder } = require('util');
if (typeof globalThis.TextEncoder === 'undefined') {
    globalThis.TextEncoder = TextEncoder;
    globalThis.TextDecoder = TextDecoder;
}