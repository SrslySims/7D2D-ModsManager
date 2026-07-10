namespace SrslyModsManager
{
    public sealed class MainMenuModsManagerButton : XUiController
    {
        public override void Init()
        {
            base.Init();
            HookButton("btnModManager");
            OnPress += delegate { OpenModManager(); };
        }

        private void HookButton(string name)
        {
            XUiController child = GetChildById(name);
            XUiController controller = child?.ViewComponent?.Controller;
            if (controller != null)
            {
                controller.OnPress += delegate { OpenModManager(); };
            }
        }

        private void OpenModManager()
        {
            if (xui?.playerUI?.windowManager == null)
            {
                return;
            }

            CloseIfOpen("mainMenu");
            CloseIfOpen("playGamePaging");
            CloseIfOpen("playerProfiles");
            CloseIfOpen("newGame");
            CloseIfOpen("continueGame");
            CloseIfOpen("serverBrowser");

            xui.playerUI.windowManager.Open(ModFolders.WindowGroup, true, false);
        }

        private void CloseIfOpen(string windowGroup)
        {
            if (xui.playerUI.windowManager.IsWindowOpen(windowGroup))
            {
                xui.playerUI.windowManager.Close(windowGroup);
            }
        }
    }
}


