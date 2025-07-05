// Trick from https://stackoverflow.com/a/77047461/190380
// When debugging ("Debug Test at Cursor" in menu), this cancels jest timeout.
if( process.env["VSCODE_INSPECTOR_OPTIONS"] ) jest.setTimeout(30 * 60 * 1000 ); // 30 minutes

// Sample test.
describe('Sample test', () => {
    it('should be true', () => {
        expect(true).toBeTruthy();
    });
    });