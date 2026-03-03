using System;
using System.Collections.Generic;
using System.IO;

namespace ConstanceModManager
{
    class ModEntry
    {
        public string Name;
        public string FileName;
        public string StoredPath;
        public bool Enabled;
        public string Version;
    }

    class Settings
    {
        public string GameExePath = "";
        public List<string> EnabledMods = new List<string>();
        public bool ShowBepInExConsole = false;
        public string Language = "en";
        public int BgIndex = 1;  // 0 = bg1, 1 = bg2 (défaut)

        static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "modmanager.cfg"); }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int sep = line.IndexOf('=');
                    if (sep < 0) continue;
                    string key = line.Substring(0, sep).Trim();
                    string val = line.Substring(sep + 1).Trim();
                    switch (key)
                    {
                        case "GameExePath": s.GameExePath = val; break;
                        case "ShowBepInExConsole": s.ShowBepInExConsole = val == "true"; break;
                        case "Language": s.Language = val; break;
                        case "BgIndex": int bi; if (int.TryParse(val, out bi)) s.BgIndex = bi; break;
                        case "EnabledMods":
                            s.EnabledMods.Clear();
                            if (val.Length > 0)
                                foreach (string m in val.Split(';'))
                                    if (m.Trim().Length > 0) s.EnabledMods.Add(m.Trim());
                            break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                List<string> lines = new List<string>();
                lines.Add("# Constance Mod Manager");
                lines.Add("GameExePath=" + GameExePath);
                lines.Add("ShowBepInExConsole=" + (ShowBepInExConsole ? "true" : "false"));
                lines.Add("Language=" + Language);
                lines.Add("BgIndex=" + BgIndex);
                lines.Add("EnabledMods=" + string.Join(";", EnabledMods.ToArray()));
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch { }
        }
    }

    static class L
    {
        public static string Lang = "fr";

        static readonly Dictionary<string, Dictionary<string, string>> _t =
            new Dictionary<string, Dictionary<string, string>>
        {
            { "fr", new Dictionary<string, string> {
                { "title",        "CONSTANCE MOD MANAGER" },
                { "subtitle",     "Boss Rush  |  Skins  |  BepInEx integre" },
                { "no_game",      "Aucun jeu selectionne  -  clique Parcourir" },
                { "browse",       "Parcourir" },
                { "status_sel",   "Selectionne l'exe du jeu" },
                { "status_ok",    "Jeu OK  |  BepInEx OK  |  {0} mod(s) actif(s)" },
                { "status_bep",   "BepInEx sera installe au premier lancement" },
                { "launched",     "Jeu lance !" },
                { "mods_title",   "MODS INSTALLES" },
                { "add",          "+ Ajouter un mod" },
                { "drop",         "Glisse tes fichiers .dll ici pour les ajouter" },
                { "launch",       "LANCER LE JEU" },
                { "footer",       "Se minimise dans le tray  |  BepInEx integre" },
                { "settings",     "Parametres" },
                { "console_opt",  "Afficher la console BepInEx au lancement" },
                { "language_lbl", "Langue" },
                { "del_confirm",  "Supprimer {0} ?" },
                { "confirm",      "Confirmer" },
                { "tray_open",    "Ouvrir" },
                { "tray_launch",  "Lancer le jeu" },
                { "tray_quit",    "Quitter" },
                { "no_mods",      "Aucun mod installe.\nGlisse une DLL ici ou clique Ajouter." },
                { "select_game",  "Selectionne l'exe du jeu" },
                { "select_dll",   "Selectionner des DLL" },
                { "bep_error",    "Impossible d'installer BepInEx :\n{0}" },
                { "bep_missing",  "BepInEx.zip introuvable dans les ressources." },
                { "bg_lbl",       "Fond d'écran" },
                { "bg1_name",     "Artwork 1" },
                { "bg2_name",     "Artwork 2" },
                { "back",         "← Retour" },
                { "settings_title", "PARAMÈTRES" },
                { "lang_fr",      "Français" },
                { "lang_en",      "Anglais" },
                { "lang_es",      "Espagnol" },
                { "lang_zh",      "Chinois" },
                { "lang_hi",      "Hindi" },
            }},
            { "en", new Dictionary<string, string> {
                { "title",        "CONSTANCE MOD MANAGER" },
                { "subtitle",     "Boss Rush  |  Skins  |  BepInEx built-in" },
                { "no_game",      "No game selected  -  click Browse" },
                { "browse",       "Browse" },
                { "status_sel",   "Select the game exe" },
                { "status_ok",    "Game OK  |  BepInEx OK  |  {0} mod(s) active" },
                { "status_bep",   "BepInEx will be installed on first launch" },
                { "launched",     "Game launched!" },
                { "mods_title",   "INSTALLED MODS" },
                { "add",          "+ Add a mod" },
                { "drop",         "Drag your .dll files here to add them" },
                { "launch",       "LAUNCH GAME" },
                { "footer",       "Minimises to tray  |  BepInEx built-in" },
                { "settings",     "Settings" },
                { "console_opt",  "Show BepInEx console on launch" },
                { "language_lbl", "Language" },
                { "del_confirm",  "Delete {0}?" },
                { "confirm",      "Confirm" },
                { "tray_open",    "Open" },
                { "tray_launch",  "Launch game" },
                { "tray_quit",    "Quit" },
                { "no_mods",      "No mods installed.\nDrag a DLL here or click Add." },
                { "select_game",  "Select the game exe" },
                { "select_dll",   "Select DLL files" },
                { "bep_error",    "Cannot install BepInEx:\n{0}" },
                { "bep_missing",  "BepInEx.zip not found in resources." },
                { "bg_lbl",       "Background" },
                { "bg1_name",     "Artwork 1" },
                { "bg2_name",     "Artwork 2" },
                { "back",         "← Back" },
                { "settings_title", "SETTINGS" },
                { "lang_fr",      "French" },
                { "lang_en",      "English" },
                { "lang_es",      "Spanish" },
                { "lang_zh",      "Chinese" },
                { "lang_hi",      "Hindi" },
            }},
            { "es", new Dictionary<string, string> {
                { "title",        "CONSTANCE MOD MANAGER" },
                { "subtitle",     "Boss Rush  |  Skins  |  BepInEx integrado" },
                { "no_game",      "Ningun juego seleccionado  -  haz clic en Examinar" },
                { "browse",       "Examinar" },
                { "status_sel",   "Selecciona el exe del juego" },
                { "status_ok",    "Juego OK  |  BepInEx OK  |  {0} mod(s) activo(s)" },
                { "status_bep",   "BepInEx se instalara en el primer lanzamiento" },
                { "launched",     "Juego iniciado!" },
                { "mods_title",   "MODS INSTALADOS" },
                { "add",          "+ Anadir mod" },
                { "drop",         "Arrastra tus archivos .dll aqui" },
                { "launch",       "INICIAR JUEGO" },
                { "footer",       "Se minimiza al tray  |  BepInEx integrado" },
                { "settings",     "Ajustes" },
                { "console_opt",  "Mostrar consola BepInEx al iniciar" },
                { "language_lbl", "Idioma" },
                { "del_confirm",  "Eliminar {0}?" },
                { "confirm",      "Confirmar" },
                { "tray_open",    "Abrir" },
                { "tray_launch",  "Iniciar juego" },
                { "tray_quit",    "Salir" },
                { "no_mods",      "Sin mods.\nArrastra un DLL o haz clic en Anadir." },
                { "select_game",  "Selecciona el exe del juego" },
                { "select_dll",   "Seleccionar archivos DLL" },
                { "bep_error",    "No se puede instalar BepInEx:\n{0}" },
                { "bep_missing",  "BepInEx.zip no encontrado en los recursos." },
                { "bg_lbl",       "Fondo" },
                { "bg1_name",     "Artwork 1" },
                { "bg2_name",     "Artwork 2" },
                { "back",         "← Volver" },
                { "settings_title", "AJUSTES" },
                { "lang_fr",      "Francés" },
                { "lang_en",      "Inglés" },
                { "lang_es",      "Español" },
                { "lang_zh",      "Chino" },
                { "lang_hi",      "Hindi" },
            }},
            { "zh", new Dictionary<string, string> {
                { "title",        "CONSTANCE MOD 管理器" },
                { "subtitle",     "Boss Rush  |  皮肤  |  内置 BepInEx" },
                { "no_game",      "未选择游戏  -  点击浏览" },
                { "browse",       "浏览" },
                { "status_sel",   "请选择游戏 exe 文件" },
                { "status_ok",    "游戏正常  |  BepInEx 正常  |  {0} 个模组已启用" },
                { "status_bep",   "首次启动时将自动安装 BepInEx" },
                { "launched",     "游戏已启动" },
                { "mods_title",   "已安装模组" },
                { "add",          "+ 添加模组" },
                { "drop",         "将 .dll 文件拖放到此处以添加" },
                { "launch",       "启动游戏" },
                { "footer",       "最小化到托盘  |  内置 BepInEx" },
                { "settings",     "设置" },
                { "console_opt",  "启动时显示 BepInEx 控制台" },
                { "language_lbl", "语言" },
                { "del_confirm",  "删除 {0}？" },
                { "confirm",      "确认" },
                { "tray_open",    "打开" },
                { "tray_launch",  "启动游戏" },
                { "tray_quit",    "退出" },
                { "no_mods",      "没有模组。\n拖放 DLL 或点击添加。" },
                { "select_game",  "选择游戏 exe 文件" },
                { "select_dll",   "选择 DLL 文件" },
                { "bep_error",    "无法安装 BepInEx：\n{0}" },
                { "bep_missing",  "资源中未找到 BepInEx.zip。" },
                { "bg_lbl",       "背景图片" },
                { "bg1_name",     "插图 1" },
                { "bg2_name",     "插图 2" },
                { "back",         "← 返回" },
                { "settings_title", "设置" },
                { "lang_fr",      "法语" },
                { "lang_en",      "英语" },
                { "lang_es",      "西班牙语" },
                { "lang_zh",      "中文" },
                { "lang_hi",      "印地语" },
            }},
            { "hi", new Dictionary<string, string> {
                { "title",        "CONSTANCE MOD MANAGER" },
                { "subtitle",     "Boss Rush  |  Skins  |  BepInEx अंतर्निहित" },
                { "no_game",      "कोई गेम नहीं चुना  -  Browse करें" },
                { "browse",       "Browse" },
                { "status_sel",   "गेम exe चुनें" },
                { "status_ok",    "गेम OK  |  BepInEx OK  |  {0} mod(s) सक्रिय" },
                { "status_bep",   "पहली बार लॉन्च पर BepInEx इंस्टॉल होगा" },
                { "launched",     "गेम शुरू हो गया!" },
                { "mods_title",   "इंस्टॉल किए गए MODS" },
                { "add",          "+ Mod जोड़ें" },
                { "drop",         ".dll फाइलें यहाँ खींचें" },
                { "launch",       "गेम लॉन्च करें" },
                { "footer",       "Tray में minimize  |  BepInEx अंतर्निहित" },
                { "settings",     "सेटिंग्स" },
                { "console_opt",  "लॉन्च पर BepInEx कंसोल दिखाएं" },
                { "language_lbl", "भाषा" },
                { "del_confirm",  "{0} हटाएं?" },
                { "confirm",      "पुष्टि करें" },
                { "tray_open",    "खोलें" },
                { "tray_launch",  "गेम लॉन्च करें" },
                { "tray_quit",    "बाहर निकलें" },
                { "no_mods",      "कोई mod नहीं।\nDLL यहाँ खींचें या जोड़ें।" },
                { "select_game",  "गेम exe चुनें" },
                { "select_dll",   "DLL फाइलें चुनें" },
                { "bep_error",    "BepInEx इंस्टॉल नहीं हो सका:\n{0}" },
                { "bep_missing",  "BepInEx.zip नहीं मिला।" },
                { "bg_lbl",       "पृष्ठभूमि" },
                { "bg1_name",     "Artwork 1" },
                { "bg2_name",     "Artwork 2" },
                { "back",         "← वापस" },
                { "settings_title", "सेटिंग्स" },
                { "lang_fr",      "फ्रेंच" },
                { "lang_en",      "अंग्रेज़ी" },
                { "lang_es",      "स्पेनिश" },
                { "lang_zh",      "चीनी" },
                { "lang_hi",      "हिन्दी" },
            }},
        };

        public static string Get(string key)
        {
            Dictionary<string, string> d;
            string v;
            if (_t.TryGetValue(Lang, out d) && d.TryGetValue(key, out v)) return v;
            if (_t.TryGetValue("en", out d) && d.TryGetValue(key, out v)) return v;
            return key;
        }
    }
}