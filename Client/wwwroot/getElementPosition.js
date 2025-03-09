window.getElementPosition = (elementId) => {
    let el = document.getElementById(elementId);
    if (!el) return null;

    let rect = el.getBoundingClientRect();
    return {
        left: rect.left,
        top: rect.top,
        right: rect.right,
        bottom: rect.bottom,
        width: rect.width,
        height: rect.height
    }
};