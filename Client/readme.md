# Grand plan

## Tasks - Bugs
- [ ] Node layout inconsistency
- [ ] Undo when zooming
- [ ] Node dragging when property widget is visible
- [ ] Deletion of nodes and links
- [ ] Layer filter auto-close 
- [ ] Make undo/redo only work on one diagram - flush when switching

## Tasks - General
- [ ] Add Beta software warning
- [ ] Scaled data loading
- [ ] View based filtering
- [ ] In-app error visualization
- [ ] Refactor layer filtering to command/handler pattern.

## Tasks - Editing
- [ ] Align left/right/top/bottom
- [ ] Read-only public diagram viewing
- [ ] Link tags
- [ ] Link directional tag
- [ ] Link filtering
- [ ] Tag colors
- [ ] Node color
- [ ] Note nodes
- [ ] Note links
- [ ] Note color
- [ ] Group
- [ ] Ungroup
- [ ] Collapse Group
- [ ] Expand Group
- [ ] Stretch
- [ ] Persist fullscreen toggle
- [ ] Other properties in property grid

## Tasks - Dependencies
- [ ] 'Future' or 'Not concrete' tag to node visualization
- [ ] Export as Text
- [ ] Export as Image
- [ ] Import / Export / Sync with Azure DevOps
- [ ] Classification: Concrete artifact, Abstract Concept, Paradigm Shift

## Potential diagrams
- [ ] Dependencies within this project
- [ ] Evolution of entertainment
- [ ] Agile project development 
- [ ] Stories => Books => Video => Games => XR
- [ ] Trends in XR
- [ ] Politics?

## Future
- [ ] Making perspectives public
- [ ] Ads
- [ ] Commerce

## Done
- [X] Filter by node tags
- [X] Export as Json
- [X] Export as Xml
- [X] Property Widget update after undo/redo
- [X] Authentication for multiple people
- [X] Ribbon disable button capability
- [X] Ribbon selection responsiveness
- [X] Ribbon node Edit
- [X] Persisted undo/redo
- [X] Node tags
- [X] Reused tags
- [X] Fullscreen toggle
- [X] Persist properties toggle
- [X] Persist navigation toggle
- [X] Link removal
- [X] Node removal

## Tools
```sql
DECLARE @sql NVARCHAR(MAX) = '';

SELECT @sql = STRING_AGG('DROP TABLE ' + QUOTENAME(TABLE_NAME), '; ')
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE';

EXEC sp_executesql @sql;
```