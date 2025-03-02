# Grand plan

## Tasks - Bugs
- [ ] Node layout inconsistency
- [ ] Undo when zooming
- [X] Authentication for multiple people

## Tasks - General
- [ ] Add Beta software warning
- [ ] Scaled data loading
- [ ] View based filtering
- [ ] In-app error visualization

## Tasks - Editing
- [X] Ribbon disable button capability
- [X] Ribbon selection responsiveness
- [X] Ribbon node Edit
- [X] Persisted undo/redo
- [ ] Node tags
- [ ] Link tags
- [ ] Tag colors
- [ ] Node color
- [ ] Note nodes
- [ ] Note links
- [ ] Note color
- [ ] Group
- [ ] Ungroup
- [ ] Collapse Group
- [ ] Expand Group
- [ ] Align left/right/top/bottom
- [ ] Stretch
- [X] Fullscreen toggle
- [ ] Persist fullscreen toggle

## Tasks - Dependencies
- [X] Link removal
- [X] Node removal
- [ ] 'Future' or 'Not concrete' tag to node visualization
- [ ] Export as Json
- [ ] Export as Xml
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

## Tools
```sql
DECLARE @sql NVARCHAR(MAX) = '';

SELECT @sql = STRING_AGG('DROP TABLE ' + QUOTENAME(TABLE_NAME), '; ')
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE';

EXEC sp_executesql @sql;
```