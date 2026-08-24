// a line comment before the workspace
# a hash comment, which the DSL also accepts
/*
 * a block comment spanning lines
 */
workspace "Comments" "Every comment form, in every position." {

    model { // trailing comment on an opening brace
        # hash comment inside the model
        u = person "User" "A user." // trailing comment on an element
        /* block comment between elements */
        s = softwareSystem "System" "A system." {
            web = container "Web App" "Serves pages." "React" // technology last
            db = container "Database" "Stores things." "PostgreSQL"
            web -> db "Reads from and writes to" "SQL/TCP"
        }
        u -> web "Visits" "HTTPS"
    }

    views {
        systemContext s "context" {
            include *
        }
        container s "containers" {
            include *
        }
    }

}
// a trailing comment after the closing brace
