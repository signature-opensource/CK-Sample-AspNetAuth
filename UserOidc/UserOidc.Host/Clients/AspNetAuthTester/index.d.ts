declare function applyEndPoint(): Promise<void>;
declare function refresh(): Promise<void>;
declare function startPopupLogin(): Promise<void>;
declare function startInlineLogin(): Promise<void>;
declare function basicLogin(): Promise<void>;
declare function logout(): Promise<void>;
declare function impersonate(): Promise<void>;
declare function unsafeDirectLogin(): Promise<void>;
declare function shrink(className: string, buttonClassName: string): void;
declare const _default: {
    refresh: typeof refresh;
    startPopupLogin: typeof startPopupLogin;
    startInlineLogin: typeof startInlineLogin;
    basicLogin: typeof basicLogin;
    logout: typeof logout;
    impersonate: typeof impersonate;
    unsafeDirectLogin: typeof unsafeDirectLogin;
    applyEndPoint: typeof applyEndPoint;
    shrink: typeof shrink;
};
export default _default;
//# sourceMappingURL=index.d.ts.map