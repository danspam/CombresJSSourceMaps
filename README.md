Combres JS SourceMaps
===================

Combres Javascript Minifier for generating [sourcemaps](http://www.html5rocks.com/en/tutorials/developertools/sourcemaps/). The minifier is based on the existing AjaxMin minifier but it also generates sourcemaps for the compressed files when not in debug mode.

## Usage

Add add a new javascript minifier to your combres.xml:

```XML
<jsMinifiers>
    <minifier name="msajaxMaps" type="CombresJSSourceMaps.SourceMapMinifier, CombresJSSourceMaps" binderType="Combres.Binders.SimpleObjectBinder, Combres">
        <param name="SourceMapOutputPath" type="string" value="~/js" />
    </minifier>
</jsMinifiers>
```

The SourceMapOutputPath is the path to where the sourcemap files will be written when the resourceSets are combined. This location must be accessible as a url from the website.

Specify the resourceSets in the combres.xml file that use this minifier:

```XML
<resourceSet name="fooJS" minifierRef="msajaxMaps" type="js">
    <resource path="~/js/foo.js" />
    <resource path="~/js/bar.js" />
</resourceSet>
```