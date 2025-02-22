window.toggleFullScreen = function () 
{
    if (!document.fullscreenElement) 
    {
        document.documentElement.requestFullscreen().catch(err => 
        {
            console.error(`Error attempting to enable full-screen mode: ${err.message}`);
        });
    } else {
        document.exitFullscreen();
    }
};
