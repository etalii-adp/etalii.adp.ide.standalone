window.stopPropagation = function (event) 
{
    event.stopPropagation();  // This prevents the click from reaching below-div
    console.log('Stopping event propagation');
};
