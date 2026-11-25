# Links example

`roadmap.tml` is copied unchanged from [etalii-adp/etalii.adp](https://github.com/etalii-adp/etalii.adp), `specifications/fbl/links-example/`, at commit `b0fd98ab` (`develop`, 2026-11-25): spec 089's worked workspace, in which a timeline, a dependency graph, a mind map and a knowledge file link to each other (FBL section 17). Etalii.adp is Apache-2.0, as this repository is.

The module tests open it to show that the served binding reads every link and the document id with no finding of an unsupported construct (etalii.adp spec 092, task T011). It does not show resolution: the other documents of the workspace are not copied, and links are resolved by the workspace's index (spec 092, pull request E).
