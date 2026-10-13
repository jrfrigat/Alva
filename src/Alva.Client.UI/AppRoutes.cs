using Microsoft.AspNetCore.Components;

namespace Alva.Client;

/// <summary>Пути внутри приложения. Оно ставится и в корень сайта, и в подпапку (демо на GitHub Pages
/// живёт в /Alva/), поэтому адреса внутри приложения относительные: их резолвит базовый адрес страницы.
/// Путь с ведущим «/» резолвится от корня хостинга мимо <base href> — так пункт меню и уводил на
/// /sprints вместо /Alva/sprints, — и ломает подсветку активного пункта, которую Flare считает через
/// ToAbsoluteUri. Тот же приём использует Flare.Gallery (Layout/NavMenu.razor).</summary>
public static class AppRoutes
{
    /// <summary>Корень приложения. Пустая ссылка резолвится в базовый адрес страницы — и в корне, и в подпапке.</summary>
    public const string Home = "";

    /// <summary>Текущий путь страницы внутри приложения, как его видит Router: «tasks», «sprints/1/plan».
    /// Базовый путь хоста отброшен, поэтому сравнения («== "my-work"», «EndsWith("/plan")») верны и в корне,
    /// и в подпапке, а параметры запроса и хвостовой «/» на них не влияют.</summary>
    public static string Current(NavigationManager navigation) =>
        navigation.ToBaseRelativePath(navigation.Uri).Split('?', '#')[0].Trim('/');
}
