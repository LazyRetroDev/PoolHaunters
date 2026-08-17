using System;
using System.Collections.Generic;
using UnityEngine;

public enum GameLanguage
{
    English,
    Portuguese,
    Spanish,
    Russian,
    French,
    German,
    Japanese,
    Chinese
}

public static class GameLocalization
{
    public const string LanguagePrefKey = "PoolHaunters.Settings.Language";

    private static readonly GameLanguage[] SupportedLanguages =
    {
        GameLanguage.English,
        GameLanguage.Portuguese,
        GameLanguage.Spanish,
        GameLanguage.Russian,
        GameLanguage.French,
        GameLanguage.German,
        GameLanguage.Japanese,
        GameLanguage.Chinese
    };

    private static readonly string[] DisplayNames =
    {
        "English",
        "Portugues",
        "Espanol",
        "Russian",
        "Francais",
        "Deutsch",
        "Japanese",
        "Chinese"
    };

    public static event Action<GameLanguage> LanguageChanged;

    public static GameLanguage CurrentLanguage { get; private set; } = LoadSavedLanguage();

    public static int CurrentLanguageIndex
    {
        get
        {
            int index = Array.IndexOf(SupportedLanguages, CurrentLanguage);
            return Mathf.Clamp(index, 0, SupportedLanguages.Length - 1);
        }
    }

    public static List<string> BuildDisplayNameList()
    {
        return new List<string>(DisplayNames);
    }

    public static void SetLanguageIndex(int index)
    {
        index = Mathf.Clamp(index, 0, SupportedLanguages.Length - 1);
        SetLanguage(SupportedLanguages[index]);
    }

    public static void SetLanguage(GameLanguage language)
    {
        if (CurrentLanguage == language)
            return;

        CurrentLanguage = language;
        PlayerPrefs.SetInt(LanguagePrefKey, (int)language);
        PlayerPrefs.Save();
        LanguageChanged?.Invoke(language);
    }

    public static string Translate(string key, string fallback = "")
    {
        if (string.IsNullOrEmpty(key))
            return fallback;

        Dictionary<string, string> table = GetTable(CurrentLanguage);
        if (table != null && table.TryGetValue(key, out string value))
            return value;

        Dictionary<string, string> english = GetTable(GameLanguage.English);
        if (english != null && english.TryGetValue(key, out string englishValue))
            return englishValue;

        return string.IsNullOrEmpty(fallback) ? key : fallback;
    }

    private static GameLanguage LoadSavedLanguage()
    {
        int saved = PlayerPrefs.GetInt(LanguagePrefKey, (int)GameLanguage.English);
        if (Enum.IsDefined(typeof(GameLanguage), saved))
            return (GameLanguage)saved;

        return GameLanguage.English;
    }

    private static Dictionary<string, string> GetTable(GameLanguage language)
    {
        switch (language)
        {
            case GameLanguage.Portuguese:
                return Portuguese;
            case GameLanguage.Spanish:
                return Spanish;
            case GameLanguage.Russian:
                return Russian;
            case GameLanguage.French:
                return French;
            case GameLanguage.German:
                return German;
            case GameLanguage.Japanese:
                return Japanese;
            case GameLanguage.Chinese:
                return Chinese;
            default:
                return English;
        }
    }

    private static readonly Dictionary<string, string> English = new Dictionary<string, string>
    {
        { "objective.findValve", "Find the water valve" },
        { "objective.turnValve", "Turn the water valve to start cleaning" },
        { "objective.cleanPools", "Clean the required pools" },
        { "objective.complete", "Objectives complete" },
        { "objective.returnToSubmarine", "Return to the Submarine Room" },
        { "objective.exitFound", "Exit found" },
        { "objective.findExit", "Find the exit" },
        { "objective.exitOptional", "Exit optional" },
        { "objective.cleaning", "Cleaning" },
        { "objective.poolCleaning", "Pool Cleaning" },
        { "objective.pools", "Pools" },
        { "objective.rooms", "Rooms" },
        { "objective.fungalPool", "Remove pool fungi: {0} left" },
        { "objective.electricPoolPowered", "Disable the electric pool device" },
        { "objective.electricPoolSafe", "Electric pool safe: {0}s until power returns" },
        { "hud.cleaningProgress", "Total Cleaning: {0}%" },
        { "hud.currentPoolProgress", "Pool Cleaning: {0}%" },
        { "hud.totalCleaningProgress", "Total Cleaning: {0}%" },
        { "hud.poolCleaningProgress", "Pool Cleaning: {0}%" },
        { "hud.pools", "Pools {0}/{1}" }
    };

    private static readonly Dictionary<string, string> Portuguese = new Dictionary<string, string>
    {
        { "objective.findValve", "Encontre a valvula de agua" },
        { "objective.turnValve", "Acione a valvula para iniciar a limpeza" },
        { "objective.cleanPools", "Limpe as piscinas obrigatorias" },
        { "objective.complete", "Objetivos concluidos" },
        { "objective.returnToSubmarine", "Volte para a Sala do Submarino" },
        { "objective.exitFound", "Saida encontrada" },
        { "objective.findExit", "Encontre a saida" },
        { "objective.exitOptional", "Saida opcional" },
        { "objective.cleaning", "Limpeza" },
        { "objective.poolCleaning", "Limpeza da piscina" },
        { "objective.pools", "Piscinas" },
        { "objective.rooms", "Salas" },
        { "objective.fungalPool", "Remova os fungos da piscina: {0} restantes" },
        { "objective.electricPoolPowered", "Desative o dispositivo da piscina eletrica" },
        { "objective.electricPoolSafe", "Piscina eletrica segura: {0}s ate a energia voltar" },
        { "hud.cleaningProgress", "Limpeza total: {0}%" },
        { "hud.currentPoolProgress", "Limpeza da piscina: {0}%" },
        { "hud.totalCleaningProgress", "Limpeza total: {0}%" },
        { "hud.poolCleaningProgress", "Limpeza da piscina: {0}%" },
        { "hud.pools", "Piscinas {0}/{1}" }
    };

    private static readonly Dictionary<string, string> Spanish = new Dictionary<string, string>
    {
        { "objective.findValve", "Encuentra la valvula de agua" },
        { "objective.turnValve", "Activa la valvula de agua para empezar a limpiar" },
        { "objective.cleanPools", "Limpia las piscinas obligatorias" },
        { "objective.complete", "Objetivos completados" },
        { "objective.returnToSubmarine", "Vuelve a la sala del submarino" },
        { "objective.exitFound", "Salida encontrada" },
        { "objective.findExit", "Encuentra la salida" },
        { "objective.exitOptional", "Salida opcional" },
        { "objective.cleaning", "Limpieza" },
        { "objective.poolCleaning", "Limpieza de piscina" },
        { "objective.pools", "Piscinas" },
        { "objective.rooms", "Salas" },
        { "objective.fungalPool", "Elimina los hongos de la piscina: quedan {0}" },
        { "objective.electricPoolPowered", "Desactiva el dispositivo de la piscina electrica" },
        { "objective.electricPoolSafe", "Piscina electrica segura: {0}s hasta que vuelva la energia" },
        { "hud.cleaningProgress", "Limpieza total: {0}%" },
        { "hud.currentPoolProgress", "Limpieza de piscina: {0}%" },
        { "hud.totalCleaningProgress", "Limpieza total: {0}%" },
        { "hud.poolCleaningProgress", "Limpieza de piscina: {0}%" },
        { "hud.pools", "Piscinas {0}/{1}" }
    };

    private static readonly Dictionary<string, string> Russian = new Dictionary<string, string>
    {
        { "objective.findValve", "Найдите водяной клапан" },
        { "objective.turnValve", "Поверните водяной клапан, чтобы начать уборку" },
        { "objective.cleanPools", "Очистите обязательные бассейны" },
        { "objective.complete", "Цели выполнены" },
        { "objective.returnToSubmarine", "Вернитесь в комнату подлодки" },
        { "objective.exitFound", "Выход найден" },
        { "objective.findExit", "Найдите выход" },
        { "objective.exitOptional", "Выход необязателен" },
        { "objective.cleaning", "Очистка" },
        { "objective.pools", "Бассейны" },
        { "objective.rooms", "Комнаты" },
        { "objective.fungalPool", "Удалите грибок в бассейне: осталось {0}" },
        { "objective.electricPoolPowered", "Отключите устройство электрического бассейна" },
        { "objective.electricPoolSafe", "Электробассейн безопасен: питание вернется через {0}с" },
        { "hud.cleaningProgress", "Очистка: {0}%" },
        { "hud.currentPoolProgress", "Текущий бассейн: {0}%" },
        { "hud.pools", "Бассейны {0}/{1}" }
    };

    private static readonly Dictionary<string, string> French = new Dictionary<string, string>
    {
        { "objective.findValve", "Trouvez la vanne d'eau" },
        { "objective.turnValve", "Activez la vanne d'eau pour commencer le nettoyage" },
        { "objective.cleanPools", "Nettoyez les piscines obligatoires" },
        { "objective.complete", "Objectifs termines" },
        { "objective.returnToSubmarine", "Retournez a la salle du sous-marin" },
        { "objective.exitFound", "Sortie trouvee" },
        { "objective.findExit", "Trouvez la sortie" },
        { "objective.exitOptional", "Sortie optionnelle" },
        { "objective.cleaning", "Nettoyage" },
        { "objective.poolCleaning", "Nettoyage piscine" },
        { "objective.pools", "Piscines" },
        { "objective.rooms", "Salles" },
        { "objective.fungalPool", "Retirez les champignons de la piscine : {0} restants" },
        { "objective.electricPoolPowered", "Desactivez le dispositif de la piscine electrique" },
        { "objective.electricPoolSafe", "Piscine electrique sure : retour du courant dans {0}s" },
        { "hud.cleaningProgress", "Nettoyage total: {0}%" },
        { "hud.currentPoolProgress", "Nettoyage piscine: {0}%" },
        { "hud.totalCleaningProgress", "Nettoyage total: {0}%" },
        { "hud.poolCleaningProgress", "Nettoyage piscine: {0}%" },
        { "hud.pools", "Piscines {0}/{1}" }
    };

    private static readonly Dictionary<string, string> German = new Dictionary<string, string>
    {
        { "objective.findValve", "Finde das Wasserventil" },
        { "objective.turnValve", "Drehe das Wasserventil auf, um mit der Reinigung zu beginnen" },
        { "objective.cleanPools", "Reinige die erforderlichen Becken" },
        { "objective.complete", "Ziele abgeschlossen" },
        { "objective.returnToSubmarine", "Kehre zum U-Boot-Raum zuruck" },
        { "objective.exitFound", "Ausgang gefunden" },
        { "objective.findExit", "Finde den Ausgang" },
        { "objective.exitOptional", "Ausgang optional" },
        { "objective.cleaning", "Reinigung" },
        { "objective.poolCleaning", "Beckenreinigung" },
        { "objective.pools", "Becken" },
        { "objective.rooms", "Raume" },
        { "objective.fungalPool", "Entferne Pilze im Becken: {0} ubrig" },
        { "objective.electricPoolPowered", "Schalte das Elektro-Becken-Gerat aus" },
        { "objective.electricPoolSafe", "Elektro-Becken sicher: Strom kehrt in {0}s zuruck" },
        { "hud.cleaningProgress", "Gesamtreinigung: {0}%" },
        { "hud.currentPoolProgress", "Beckenreinigung: {0}%" },
        { "hud.totalCleaningProgress", "Gesamtreinigung: {0}%" },
        { "hud.poolCleaningProgress", "Beckenreinigung: {0}%" },
        { "hud.pools", "Becken {0}/{1}" }
    };

    private static readonly Dictionary<string, string> Japanese = new Dictionary<string, string>
    {
        { "objective.findValve", "水栓を探す" },
        { "objective.turnValve", "水栓を開いて清掃を始める" },
        { "objective.cleanPools", "必要なプールを清掃する" },
        { "objective.complete", "目標達成" },
        { "objective.returnToSubmarine", "潜水艦ルームへ戻る" },
        { "objective.exitFound", "出口発見" },
        { "objective.findExit", "出口を探す" },
        { "objective.exitOptional", "出口は任意" },
        { "objective.cleaning", "清掃" },
        { "objective.pools", "プール" },
        { "objective.rooms", "部屋" },
        { "objective.fungalPool", "プールの菌を除去: 残り{0}" },
        { "objective.electricPoolPowered", "電気プール装置を止める" },
        { "objective.electricPoolSafe", "電気プール安全: 復電まで{0}秒" },
        { "hud.cleaningProgress", "清掃: {0}%" },
        { "hud.currentPoolProgress", "現在のプール: {0}%" },
        { "hud.pools", "プール {0}/{1}" }
    };

    private static readonly Dictionary<string, string> Chinese = new Dictionary<string, string>
    {
        { "objective.findValve", "找到水阀" },
        { "objective.turnValve", "打开水阀开始清洁" },
        { "objective.cleanPools", "清洁必需的泳池" },
        { "objective.complete", "目标完成" },
        { "objective.returnToSubmarine", "返回潜艇房间" },
        { "objective.exitFound", "已找到出口" },
        { "objective.findExit", "找到出口" },
        { "objective.exitOptional", "出口可选" },
        { "objective.cleaning", "清洁" },
        { "objective.pools", "泳池" },
        { "objective.rooms", "房间" },
        { "objective.fungalPool", "清除泳池真菌：剩余 {0}" },
        { "objective.electricPoolPowered", "关闭电泳池装置" },
        { "objective.electricPoolSafe", "电泳池安全：{0} 秒后恢复供电" },
        { "hud.cleaningProgress", "清洁: {0}%" },
        { "hud.currentPoolProgress", "当前泳池: {0}%" },
        { "hud.pools", "泳池 {0}/{1}" }
    };
}
