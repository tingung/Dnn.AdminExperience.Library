﻿using System;
using System.Linq;
using System.Web.UI.WebControls;
using Dnn.PersonaBar.Extensions.Components.Dto;
using Dnn.PersonaBar.Extensions.Components.Dto.Editors;
using DotNetNuke.Common;
using DotNetNuke.Entities.Modules;
using DotNetNuke.Entities.Portals;
using DotNetNuke.Entities.Tabs;
using DotNetNuke.Entities.Users;
using DotNetNuke.Instrumentation;
using DotNetNuke.Services.Authentication;
using DotNetNuke.Services.Authentication.OAuth;
using DotNetNuke.Services.Installer.Packages;

namespace Dnn.PersonaBar.Extensions.Components.Editors
{
    public class AuthSystemPackageEditor : IPackageEditor
    {
        private static readonly ILog Logger = LoggerSource.Instance.GetLogger(typeof(AuthSystemPackageEditor));

        #region IPackageEditor Implementation

        public PackageInfoDto GetPackageDetail(int portalId, PackageInfo package)
        {
            var authSystem = AuthenticationController.GetAuthenticationServiceByPackageID(package.PackageID);
            var detail = new AuthSystemPackageDetailDto(portalId, package)
            {
                AuthenticationType = authSystem.AuthenticationType,
            };

            var isHostUser = UserController.Instance.GetCurrentUserInfo().IsSuperUser;
            if (isHostUser)
            {
                detail.ReadOnly |= authSystem.AuthenticationType == "DNN";
                detail.LoginControlSource = authSystem.LoginControlSrc;
                detail.LogoffControlSource = authSystem.LogoffControlSrc;
                detail.SettingsControlSource = authSystem.SettingsControlSrc;
                detail.Enabled = authSystem.IsEnabled;
            }

            LoadCustomSettings(portalId, package, authSystem, detail);
            return detail;
        }

        public bool SavePackageSettings(PackageSettingsDto packageSettings, out string errorMessage)
        {
            errorMessage = string.Empty;

            try
            {
                var isHostUser = UserController.Instance.GetCurrentUserInfo().IsSuperUser;

                if (isHostUser)
                {
                    string value;
                    var authSystem = AuthenticationController.GetAuthenticationServiceByPackageID(packageSettings.PackageId);

                    if (packageSettings.EditorActions.TryGetValue("loginControlSource", out value)
                        && !string.IsNullOrEmpty(value))
                    {
                        authSystem.LoginControlSrc = value;
                    }
                    if (packageSettings.EditorActions.TryGetValue("logoffControlSource", out value)
                        && !string.IsNullOrEmpty(value))
                    {
                        authSystem.LogoffControlSrc = value;
                    }
                    if (packageSettings.EditorActions.TryGetValue("settingsControlSource", out value)
                        && !string.IsNullOrEmpty(value))
                    {
                        authSystem.SettingsControlSrc = value;
                    }
                    if (packageSettings.EditorActions.TryGetValue("enabled", out value)
                        && !string.IsNullOrEmpty(value))
                    {
                        authSystem.IsEnabled = bool.Parse(value);
                    }

                    AuthenticationController.UpdateAuthentication(authSystem);
                    SaveCustomSettings(packageSettings);
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                errorMessage = ex.Message;
                return false;
            }
        }

        #endregion

        #region Private Methods

        private static string GetSettingUrl(int portalId, int authSystemPackageId)
        {
            var module = ModuleController.Instance.GetModulesByDefinition(portalId, "Extensions")
                .Cast<ModuleInfo>().FirstOrDefault();
            if (module == null)
            {
                return string.Empty;
            }

            var tabId = TabController.Instance.GetTabsByModuleID(module.ModuleID).Keys.FirstOrDefault();
            if (tabId <= 0)
            {
                return string.Empty;
            }

            // Ex.: /Admin/Extensions/ctl/Edit/mid/##/packageid/##/mode/settings?popUp=true
            return Globals.NavigateURL(tabId, PortalSettings.Current, "Edit",
                "mid=" + module.ModuleID,
                "packageid=" + authSystemPackageId,
                "popUp=true",
                "mode=settings");
        }

        private static void LoadCustomSettings(int portalId, PackageInfo package, AuthenticationInfo authSystem, AuthSystemPackageDetailDto detail)
        {
            // special case for DNN provided external authentication systems
            switch (detail.AuthenticationType.ToLowerInvariant())
            {
                case "dnnpro_activedirectory":
                    var hasCustomSettings = !string.IsNullOrEmpty(authSystem.SettingsControlSrc);
                    if (hasCustomSettings)
                    {
                        detail.SettingUrl = GetSettingUrl(portalId, package.PackageID);
                    }
                    break;
                case "facebook":
                case "google":
                case "live":
                case "twitter":
                    var config = OAuthConfigBase.GetConfig(detail.AuthenticationType, portalId);
                    if (config != null)
                    {
                        detail.AppId = config.APIKey;
                        detail.AppSecret = config.APISecret;
                        detail.AppEnabled = config.Enabled;
                    }
                    break;
            }
        }

        private static void SaveCustomSettings(PackageSettingsDto packageSettings)
        {
            // special case for specific DNN provided external authentication systems
            string authType;
            if (packageSettings.EditorActions.TryGetValue("authenticationType", out authType))
            {
                switch (authType.ToLowerInvariant())
                {
                    case "facebook":
                    case "google":
                    case "live":
                    case "twitter":
                        var dirty = false;
                        string value;
                        var config = OAuthConfigBase.GetConfig(authType, packageSettings.PortalId);

                        if (packageSettings.EditorActions.TryGetValue("appId", out value)
                            && config.APIKey != value)
                        {
                            config.APIKey = value;
                            dirty = true;
                        }

                        if (packageSettings.EditorActions.TryGetValue("appSecret", out value)
                            && config.APISecret != value)
                        {
                            config.APISecret = value;
                            dirty = true;
                        }

                        if (packageSettings.EditorActions.TryGetValue("appEnabled", out value)
                            && config.Enabled.ToString().ToUpperInvariant() != value.ToUpperInvariant())
                        {
                            config.Enabled = "TRUE".Equals(value, StringComparison.InvariantCultureIgnoreCase);
                            dirty = true;
                        }

                        if (dirty) OAuthConfigBase.UpdateConfig(config);
                        break;
                }
            }
        }

        #endregion
    }
}