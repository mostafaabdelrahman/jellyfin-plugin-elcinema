# plugin for metadata providers icons

```js
@import url("https://cdn.jsdelivr.net/gh/Druidblack/jellyfin-icon-metadata@main/public-icon.css");



// elcinema.com
.itemExternalLinks a[href*="elcinema.com"] {
    background: none !important;
    color: transparent !important;
    padding: 0 !important;
}

.itemExternalLinks a[href*="elcinema.com"]::before {
    content: "";
    display: inline-block;
    width: 90px;
    height: 25px;
    background-image: url('https://thumb.wikimedia.org/wikipedia/commons/thumb/a/ad/Elcinema.com_Logo.svg/1280px-Elcinema.com_Logo.svg.png?utm_source=ar.wikipedia.org&utm_campaign=index&utm_content=thumbnail');
    background-size: contain;
    background-repeat: no-repeat;
    margin: 1px 5px 1px 5px;
    vertical-align: middle;
}

.layout-desktop .itemExternalLinks.focuscontainer-x > a[href*="elcinema.com"]:hover,
.layout-desktop .itemExternalLinks.focuscontainer-x > a[href*="elcinema.com"]:focus,
.layout-desktop .itemExternalLinks.focuscontainer-x > a[href*="elcinema.com"]:active {
    background: none !important;
    background-color: white !important;
    background-image: none !important;
    filter: none !important;
    border-color: transparent !important;
}

.itemExternalLinks a[href*="elcinema.com"] {
    font-size: 0;
    background-color: white !important;
    margin-right: 5px;
}

```


## Usefull API endpoints
List actors

https://jellyfin.home.mostafaahmed.com/Persons?personTypes=Actor&limit=5000&startIndex=0&Fields=ProviderIds

https://jellyfin.home.mostafaahmed.com/Persons?limit=5000&startIndex=0&Fields=ProviderIds

