using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// A small but genuine Ansible project, written into a temp folder for the integration flows.
/// </summary>
/// <remarks>
/// Written here rather than copied from the module's own <c>Fixtures/</c>: test projects do not
/// reference one another, and the flows need only enough of a project to prove that core routes
/// a folder-subject registration and streams what the module makes of it. The module's own tests
/// are where the full best-practice tree earns its keep.
/// </remarks>
internal static class AnsibleFixture
{
    public static void CopyTo(string folder)
    {
        Write(folder, "ansible.cfg", "[defaults]\ninventory = inventories/production\nroles_path = roles\n");

        Write(folder, "site.yml", "---\n- import_playbook: webservers.yml\n");
        Write(folder, "webservers.yml", "---\n- name: Configure the web tier\n  hosts: web\n  roles:\n    - common\n    - nginx\n");

        Write(folder, IoPath.Combine("inventories", "production", "hosts.yml"),
            "---\nall:\n  children:\n    web:\n      hosts:\n        web-01.example.com:\n        web-02.example.com:\n");
        Write(folder, IoPath.Combine("inventories", "production", "group_vars", "all.yml"), "---\nntp_server: ntp.example.com\n");

        Write(folder, IoPath.Combine("roles", "common", "tasks", "main.yml"),
            "---\n- name: Install the base packages\n  ansible.builtin.package:\n    name: rsync\n    state: present\n");

        Write(folder, IoPath.Combine("roles", "nginx", "tasks", "main.yml"),
            "---\n- name: Install nginx\n  ansible.builtin.package:\n    name: nginx\n    state: present\n\n"
            + "- name: Configure TLS\n  ansible.builtin.include_tasks: tls.yml\n  when: nginx_tls_enabled | default(false)\n");
        Write(folder, IoPath.Combine("roles", "nginx", "tasks", "tls.yml"),
            "---\n- name: Create the certificate directory\n  ansible.builtin.file:\n    path: /etc/nginx/tls\n    state: directory\n    mode: \"0700\"\n");
        Write(folder, IoPath.Combine("roles", "nginx", "handlers", "main.yml"),
            "---\n- name: Restart nginx\n  ansible.builtin.service:\n    name: nginx\n    state: restarted\n");
        Write(folder, IoPath.Combine("roles", "nginx", "meta", "main.yml"), "---\ndependencies:\n  - role: common\n");
    }

    /// <summary>
    /// The same project with one mistake in it: a playbook naming a role that has no folder.
    /// Enough for the validation flow to have exactly one thing to find.
    /// </summary>
    public static void CopyBrokenTo(string folder)
    {
        CopyTo(folder);
        Write(folder, "webservers.yml", "---\n- name: Configure the web tier\n  hosts: web\n  roles:\n    - common\n    - absent-role\n");
    }

    /// <summary>Repairs the mistake <see cref="CopyBrokenTo"/> made, as a user editing the file would.</summary>
    public static void RepairIn(string folder) =>
        Write(folder, "webservers.yml", "---\n- name: Configure the web tier\n  hosts: web\n  roles:\n    - common\n    - nginx\n");

    private static void Write(string folder, string relativePath, string content)
    {
        var path = IoPath.Combine(folder, relativePath);
        Directory.CreateDirectory(IoPath.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
