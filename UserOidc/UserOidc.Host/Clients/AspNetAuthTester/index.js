import { AuthService } from './ck-gen';
import axios from 'axios';
//Initialize a new auth service
let identityEndPoint = {
    hostname: 'localhost',
    port: 5001,
    disableSsl: true
};
// We use one axios instance here.
const axiosInstance = axios.create();
const onAuthChange = (e) => updateDisplay();
let authService;
let requestCounter = 0;
function startActivity() {
    if (++requestCounter == 1)
        authServiceHeader.className = "blinking";
}
function stopActivity() {
    if (--requestCounter == 0)
        authServiceHeader.className = "";
}
async function applyEndPoint() {
    startActivity();
    epApply.disabled = true;
    refreshSend.disabled = true;
    popupLoginSend.disabled = true;
    logoutSend.disabled = true;
    if (authService) {
        authService.removeOnChange(onAuthChange);
        authService.close();
    }
    authService = await AuthService.createAsync({
        identityEndPoint: {
            hostname: epHostName.value,
            port: Number.parseInt(epPort.value),
            disableSsl: epDisableSsl.checked
        },
        useLocalStorage: epUseLocalStorage.checked
    }, axiosInstance);
    authService.addOnChange(onAuthChange);
    stopActivity();
    updateDisplay();
    configName.innerHTML = "Configuration";
    epApply.disabled = false;
    refreshSend.disabled = false;
    popupLoginSend.disabled = false;
    logoutSend.disabled = false;
}
async function refresh() {
    startActivity();
    await authService.refresh(refreshFull.checked, refreshSchemes.checked, refreshVersion.checked);
    stopActivity();
}
async function startPopupLogin() {
    startActivity();
    await authService.startPopupLogin(popupLoginSchemes.value, popupLoginRememberMe.checked, popupLoginImpersonateAsCurrentUser.checked, !!popupLoginUserData.value ? JSON.parse(popupLoginUserData.value) : undefined);
    stopActivity();
}
async function startInlineLogin() {
    startActivity();
    await authService.startInlineLogin(inlineLoginScheme.value, inlineLoginReturnUrl.value, inlineLoginRememberMe.checked, inlineLoginImpersonateAsCurrentUser.checked, !!inlineLoginUserData.value ? JSON.parse(inlineLoginUserData.value) : undefined);
    stopActivity();
}
async function basicLogin() {
    startActivity();
    await authService.basicLogin(basicLoginUserName.value, basicLoginPassword.value, basicLoginRememberMe.checked, basicLoginImpersonateAsCurrentUser.checked, !!basicLoginUserData.value ? JSON.parse(basicLoginUserData.value) : undefined);
    stopActivity();
}
async function logout() {
    startActivity();
    await authService.logout();
    stopActivity();
}
async function impersonate() {
    startActivity();
    if (impersonateByUserId.checked) {
        const id = parseInt(impersonateUserIdOrName.value);
        if (isNaN(id))
            alert('UserId must be an integer.');
        else
            await authService.impersonate(id);
    }
    else {
        const n = impersonateUserIdOrName.value;
        await authService.impersonate(impersonateUserIdOrName.value);
    }
    stopActivity();
}
async function unsafeDirectLogin() {
    startActivity();
    await authService.unsafeDirectLogin(unsafeDirectLoginProvider.value, !!unsafeDirectLoginPayload.value ? JSON.parse(unsafeDirectLoginPayload.value) : undefined, unsafeDirectLoginRememberMe.checked, unsafeDirectLoginImpersonateAsCurrentUser.checked);
    stopActivity();
}
function shrink(className, buttonClassName) {
    let shrinkingContent = document.getElementById(className);
    let button = document.getElementById(buttonClassName);
    if (shrinkingContent.style.display != "none") {
        shrinkingContent.style.display = "none";
        button.style.transform = "rotateZ(-90deg)";
    }
    else {
        shrinkingContent.style.display = "flex";
        button.style.transform = "rotateZ(90deg)";
    }
}
let authServiceHeader;
let authServiceJson;
let configName;
let epHostName;
let epPort;
let epDisableSsl;
let epUseLocalStorage;
let epCheckEndPointVersion;
let epApply;
let userAuthLevel;
let userId;
let userActualUser;
let refreshFull;
let refreshSchemes;
let refreshVersion;
let refreshSend;
let popupLoginSchemes;
let popupLoginRememberMe;
let popupLoginImpersonateAsCurrentUser;
let popupLoginUserData;
let popupLoginSend;
let inlineLoginScheme;
let inlineLoginRememberMe;
let inlineLoginImpersonateAsCurrentUser;
let inlineLoginReturnUrl;
let inlineLoginUserData;
let inlineLoginSend;
let basicLoginUserName;
let basicLoginRememberMe;
let basicLoginImpersonateAsCurrentUser;
let basicLoginPassword;
let basicLoginUserData;
let basicLoginSend;
let impersonateUserIdOrName;
let impersonateSend;
let impersonateByUserId;
let unsafeDirectLoginProvider;
let unsafeDirectLoginRememberMe;
let unsafeDirectLoginImpersonateAsCurrentUser;
let unsafeDirectLoginPayload;
let unsafeDirectLoginSend;
let logoutSend;
document.onreadystatechange = async () => {
    if (document.readyState !== "complete")
        return;
    authServiceHeader = document.getElementById("authServiceHeader");
    authServiceJson = document.getElementById("authServiceJson");
    configName = document.getElementById("configName");
    epHostName = document.getElementById("epHostName");
    epPort = document.getElementById("epPort");
    epDisableSsl = document.getElementById("epDisableSsl");
    epUseLocalStorage = document.getElementById("epUseLocalStorage");
    epCheckEndPointVersion = document.getElementById("epCheckEndPointVersion");
    epApply = document.getElementById("epApply");
    userAuthLevel = document.getElementById("userAuthLevel");
    userId = document.getElementById("userId");
    userActualUser = document.getElementById("userActualUser");
    refreshFull = document.getElementById("refreshFull");
    refreshSchemes = document.getElementById("refreshSchemes");
    refreshVersion = document.getElementById("refreshVersion");
    refreshSend = document.getElementById("refreshSend");
    popupLoginSchemes = document.getElementById("popupLoginSchemes");
    popupLoginRememberMe = document.getElementById("popupLoginRememberMe");
    popupLoginImpersonateAsCurrentUser = document.getElementById("popupLoginImpersonateAsCurrentUser");
    popupLoginUserData = document.getElementById("popupLoginUserData");
    popupLoginSend = document.getElementById("popupLoginSend");
    inlineLoginScheme = document.getElementById("inlineLoginScheme");
    inlineLoginRememberMe = document.getElementById("inlineLoginRememberMe");
    inlineLoginImpersonateAsCurrentUser = document.getElementById("inlineLoginImpersonateAsCurrentUser");
    inlineLoginReturnUrl = document.getElementById("inlineLoginReturnUrl");
    inlineLoginUserData = document.getElementById("inlineLoginUserData");
    inlineLoginSend = document.getElementById("inlineLoginSend");
    basicLoginUserName = document.getElementById("basicLoginUserName");
    basicLoginRememberMe = document.getElementById("basicLoginRememberMe");
    basicLoginImpersonateAsCurrentUser = document.getElementById("basicLoginImpersonateAsCurrentUser");
    basicLoginPassword = document.getElementById("basicLoginPassword");
    basicLoginUserData = document.getElementById("basicLoginUserData");
    basicLoginSend = document.getElementById("basicLoginSend");
    impersonateUserIdOrName = document.getElementById("impersonateUserIdOrName");
    impersonateByUserId = document.getElementById("impersonateByUserId");
    impersonateSend = document.getElementById("impersonateSend");
    unsafeDirectLoginProvider = document.getElementById("unsafeDirectLoginProvider");
    unsafeDirectLoginRememberMe = document.getElementById("unsafeDirectLoginRememberMe");
    unsafeDirectLoginImpersonateAsCurrentUser = document.getElementById("unsafeDirectLoginImpersonateAsCurrentUser");
    unsafeDirectLoginPayload = document.getElementById("unsafeDirectLoginPayload");
    unsafeDirectLoginSend = document.getElementById("unsafeDirectLoginSend");
    logoutSend = document.getElementById("logoutSend");
    await applyEndPoint();
};
async function updateDisplay() {
    const clean = {
        authenticationInfo: authService.authenticationInfo,
        availableSchemes: authService.availableSchemes,
        endPointVersion: authService.endPointVersion,
        refreshable: authService.refreshable,
        rememberMe: authService.rememberMe,
        token: authService.token,
        lastResult: authService.lastResult
    };
    authServiceJson.innerText = JSON.stringify(clean, undefined, 3);
    popupLoginSchemes.innerHTML = "";
    inlineLoginScheme.innerHTML = "";
    if (authService.availableSchemes.includes("Basic"))
        basicLoginSend.disabled = false;
    else
        basicLoginSend.disabled = true;
    authService.availableSchemes.forEach(scheme => {
        popupLoginSchemes.options.add(new Option(scheme));
        inlineLoginScheme.options.add(new Option(scheme));
    });
    let authLevelText;
    switch (authService.authenticationInfo.level) {
        case 0:
            authLevelText = "None";
            userAuthLevel.style.backgroundColor = "#000";
            break;
        case 1:
            authLevelText = "Unsafe";
            userAuthLevel.style.backgroundColor = "red";
            break;
        case 2:
            authLevelText = "Normal";
            userAuthLevel.style.backgroundColor = "green";
            break;
        case 3:
            authLevelText = "Critical";
            userAuthLevel.style.backgroundColor = "blue";
            break;
        default:
            authLevelText = "None";
            userAuthLevel.style.backgroundColor = "#000";
    }
    userAuthLevel.innerHTML = authLevelText;
    userId.innerText = authService.authenticationInfo.user.userName + " (Id: " + authService.authenticationInfo.user.userId.toString() + ")";
    userActualUser.innerText = authService.authenticationInfo.actualUser.userName + " (Id: " + authService.authenticationInfo.actualUser.userId.toString() + ")";
    if (!authService.authenticationInfo.isImpersonated && userActualUser.parentElement) {
        userActualUser.parentElement.remove();
    }
}
export default {
    refresh,
    startPopupLogin,
    startInlineLogin,
    basicLogin,
    logout,
    impersonate,
    unsafeDirectLogin,
    applyEndPoint,
    shrink
};
//# sourceMappingURL=index.js.map