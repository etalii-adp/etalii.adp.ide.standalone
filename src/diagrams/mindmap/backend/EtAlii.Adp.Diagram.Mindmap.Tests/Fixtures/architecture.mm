<map version="freeplane 1.11.5">
<!--To view this file, download free mind mapping software Freeplane from https://www.freeplane.org-->
<node TEXT="ADP architecture" FOLDED="false" ID="ID_1730044821" CREATED="1755820800000" MODIFIED="1755907200000" STYLE="oval">
<font SIZE="18"/>
<hook NAME="MapStyle" zoom="0.909">
    <properties edgeColorConfiguration="#808080ff,#ff0000ff,#0000ffff,#00ff00ff,#ff00ffff,#00ffffff,#7c0000ff" associatedTemplateLocation="template:/standard-1.6.mm" fit_to_viewport="false" show_note_icons="true"/>
    <map_styles>
        <stylenode LOCALIZED_TEXT="styles.root_node" STYLE="oval" UNIFORM_SHAPE="true" VGAP_QUANTITY="24 pt">
            <font SIZE="24"/>
            <stylenode LOCALIZED_TEXT="defaultstyle.details"/>
            <stylenode LOCALIZED_TEXT="defaultstyle.note" COLOR="#000000" BACKGROUND_COLOR="#ffffff" TEXT_ALIGN="LEFT"/>
            <stylenode LOCALIZED_TEXT="styles.topic" COLOR="#18898b" STYLE="fork">
                <font NAME="Liberation Sans" SIZE="10" BOLD="true"/>
            </stylenode>
        </stylenode>
    </map_styles>
</hook>
<hook NAME="AutomaticEdgeColor" COUNTER="5" RULE="ON_BRANCH_CREATION"/>
<richcontent TYPE="NOTE" CONTENT-TYPE="xml/">
<html>
  <head/>
  <body>
    <p>The map ADP's round-trip tests read. Keep it ugly on purpose &#8212; it is here to be hard.</p>
  </body>
</html>
</richcontent>
<node TEXT="Backend" POSITION="right" ID="ID_411002937" CREATED="1755820801000" MODIFIED="1755906000000">
<edge COLOR="#ff0000" WIDTH="2"/>
<icon BUILTIN="gohome"/>
<attribute NAME="owner" VALUE="platform"/>
<attribute NAME="reviewed" VALUE="2026-08-19"/>
<node TEXT="Context service" ID="ID_88117420" CREATED="1755820802000" MODIFIED="1755820900000" LINK="../../../../backend/EtAlii.Adp.Backend/Context/ContextServiceImpl.cs">
<richcontent TYPE="NOTE">
<html>
  <head/>
  <body>
    <p>Owns the selection. See <b>tech.md</b> &#8250; Context.</p>
    <p>Nested markup &amp; entities live here: &lt;node&gt; is not a node.</p>
  </body>
</html>
</richcontent>
</node>
<node TEXT="Commands &amp; history" ID="ID_88117421" CREATED="1755820803000" MODIFIED="1755820903000" LINK="../../../../backend/EtAlii.Adp.Backend/History/">
<cloud COLOR="#f0f0f0" SHAPE="ARC"/>
<node TEXT="ICommand" ID="ID_88117422" CREATED="1755820804000" MODIFIED="1755820904000"/>
<node TEXT="ICommandHandler&lt;T&gt;" ID="ID_88117423" CREATED="1755820805000" MODIFIED="1755820905000"/>
<node TEXT="IHistoryStack" ID="ID_88117424" CREATED="1755820806000" MODIFIED="1755820906000"/>
</node>
<node TEXT="Hierarchy" FOLDED="true" ID="ID_88117425" CREATED="1755820807000" MODIFIED="1755820907000">
<node TEXT="HierarchyModel" ID="ID_88117426" CREATED="1755820808000" MODIFIED="1755820908000"/>
<node TEXT="RootFolderWatcher" ID="ID_88117427" CREATED="1755820809000" MODIFIED="1755820909000">
<node TEXT="buffer overflow &#8594; reconcile" ID="ID_88117428" CREATED="1755820810000" MODIFIED="1755820910000"/>
</node>
</node>
</node>
<node TEXT="Client" POSITION="right" ID="ID_411002938" CREATED="1755820811000" MODIFIED="1755820911000">
<edge COLOR="#0000ff"/>
<node TEXT="" ID="ID_411002939" CREATED="1755820812000" MODIFIED="1755820912000"/>
<node ID="ID_411002940" CREATED="1755820813000" MODIFIED="1755820913000">
<richcontent TYPE="NODE" CONTENT-TYPE="xml/">
<html>
  <head/>
  <body>
    <p>A node whose text is <i>rich content</i> rather than a TEXT attribute</p>
  </body>
</html>
</richcontent>
</node>
<node TEXT="Ribbon" CREATED="1755820814000" MODIFIED="1755820914000">
<node TEXT="a child of a node that has no ID attribute" CREATED="1755820815000" MODIFIED="1755820915000"/>
</node>
</node>
<node TEXT="Diagram types" POSITION="left" ID="ID_411002941" CREATED="1755820816000" MODIFIED="1755820916000">
<edge COLOR="#00ff00"/>
<arrowlink DESTINATION="ID_411002937" STARTINCLINATION="86;0;" ENDINCLINATION="86;0;" STARTARROW="NONE" ENDARROW="DEFAULT"/>
<node TEXT="freeplane/mindmap" ID="ID_411002942" CREATED="1755820817000" MODIFIED="1755820917000" LINK="https://www.freeplane.org/wiki/index.php/Current_Version">
<attribute NAME="state" VALUE="specified"/>
</node>
<node TEXT="c4/context" ID="ID_411002943" CREATED="1755820818000" MODIFIED="1755820918000"/>
<node TEXT="Ünïcödé — dash, curly &#8216;quotes&#8217;, emoji &#127760;" ID="ID_411002944" CREATED="1755820819000" MODIFIED="1755820919000"/>
</node>
<node TEXT="Deliberately deep" POSITION="left" ID="ID_411002945" CREATED="1755820820000" MODIFIED="1755820920000">
<node TEXT="level 2" ID="ID_411002946" CREATED="1755820821000" MODIFIED="1755820921000">
<node TEXT="level 3" ID="ID_411002947" CREATED="1755820822000" MODIFIED="1755820922000">
<node TEXT="level 4" ID="ID_411002948" CREATED="1755820823000" MODIFIED="1755820923000">
<node TEXT="level 5" ID="ID_411002949" CREATED="1755820824000" MODIFIED="1755820924000"/>
</node>
</node>
</node>
</node>
</node>
</map>
