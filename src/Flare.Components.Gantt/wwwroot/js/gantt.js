// Flare Gantt: what the timeline cannot know from .NET alone — how wide it is on screen, the drag of the
// row-header column's edge, and the horizontal scroll position.

const MinSide = 120;

export function attach(root, dotnet) {
    const scroll = root.querySelector('.flare-gantt__scroll');
    const corner = root.querySelector('.flare-gantt__corner');
    let resizing = null;

    const side = () => corner ? corner.getBoundingClientRect().width : 0;
    const report = () => {
        if (resizing) return;
        dotnet.invokeMethodAsync('Viewport', scroll.clientWidth, side()).catch(() => { });
    };
    const observer = new ResizeObserver(report);
    observer.observe(scroll);
    // Первый замер может прийтись на ещё не устоявшуюся раскладку страницы — повторяем его, когда она доделана.
    report();
    const settle = setTimeout(report, 300);
    window.addEventListener('resize', report);

    // The column follows the pointer through the CSS variable alone; .NET hears only the final width.
    const down = e => {
        const handle = e.target.closest('.flare-gantt__side-handle');
        if (!handle || e.button !== 0) return;
        e.preventDefault();
        // Захват держит перетаскивание, даже если указатель ушёл с узкой ручки; без него хватает слушателей на корне.
        try { handle.setPointerCapture(e.pointerId); } catch { }
        resizing = { handle, x: e.clientX, width: side(), current: side() };
    };
    const move = e => {
        if (!resizing) return;
        const max = Math.max(MinSide, scroll.clientWidth * 0.7);
        resizing.current = Math.round(Math.min(max, Math.max(MinSide, resizing.width + e.clientX - resizing.x)));
        root.style.setProperty('--flare-gantt-side', resizing.current + 'px');
    };
    const up = () => {
        if (!resizing) return;
        const width = resizing.current;
        resizing = null;
        dotnet.invokeMethodAsync('SideResized', width).catch(() => { });
        report();
    };
    root.addEventListener('pointerdown', down);
    root.addEventListener('pointermove', move);
    root.addEventListener('pointerup', up);
    root.addEventListener('pointercancel', up);

    return {
        // Пока панели дорисовываются, ось ещё узка и браузер обрежет прокрутку — повторяем, когда ширина готова.
        scrollTo(x) {
            const apply = () => { scroll.scrollLeft = Math.max(0, x); };
            apply();
            setTimeout(apply, 300);
        },
        dispose() {
            observer.disconnect();
            clearTimeout(settle);
            window.removeEventListener('resize', report);
            root.removeEventListener('pointerdown', down);
            root.removeEventListener('pointermove', move);
            root.removeEventListener('pointerup', up);
            root.removeEventListener('pointercancel', up);
        }
    };
}
