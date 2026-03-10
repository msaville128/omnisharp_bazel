# C# Language Services for Bazel
This is a plugin for [OmniSharp](https://github.com/OmniSharp/omnisharp-roslyn) that adds language services for C# code in a Bazel repo.

With it, you don't need `.csproj`, `.sln`, or other MSBuild files to coexist in your repo to receive language services like auto-complete, semantic highlighting, and more!

This plugin is still in early development. I cannot promise that it's stable yet, but I've successfully used it myself for 2+ months now. All feedback, bug reports, and pull requests are appreciated!

## Compatibility

It works with any Bazel rule that directly calls the C# compiler `csc` as one of its build actions.

| Module       | Rule           | Compatible |
| ------------ | -------------- | ---------- |
| rules_dotnet | [csharp_library](https://registry.bazel.build/docs/rules_dotnet#rule-csharp_library) | Yes |
| rules_dotnet | [csharp_binary](https://registry.bazel.build/docs/rules_dotnet#rule-csharp_binary)  | Yes |

The plugin is untested on Windows. It should work, but please create an issue if it doesn't.

## Setup

You must have `omnisharp.json` in the root directory of your repo with a path to the plugin dll. This path is different based on which method you use to [install the plugin](#installation).

Disable other project systems to avoid conflicts, as shown in the example config below.

### omnisharp.json
```json
{
    "plugins": {
        "locationPaths": [
            "path/to/plugin.dll"
        ]
    },
    "bazel": {
        "executable": "bazelisk",
        "enabled": true
    },
    "msbuild": {
        "enabled": false
    },
    "cake": {
        "enabled": false
    },
    "script": {
        "enabled": false
    }
}
```

Make sure `executable` is set to the name or path to the Bazel executable in your environment. The default value is `bazelisk` as that's what most users have.

## Installation

Pick one of the methods below to install the plugin, and then follow the instructions in [IDE Integration](#ide-integration).

### Bzlmod (recommended)

> [!NOTE]
> I'm working on publishing this to Bazel Central Registry. For now, `git_override` is required.

1. Add this to `MODULE.bazel`:

   ```python
   bazel_dep(name = "omnisharp_bazel", version = "1.0.0")
   git_override(
       module_name = "omnisharp_bazel",
       remote = "https://github.com/msaville128/omnisharp_bazel",
       commit = "ab542c41544663b343470bc1b2b3c57984ae83fe"
   )
   ```

2. Run `bazel build @omnisharp_bazel//omnisharp_bazel`

3. Copy the path to the dll from the output into `omnisharp.json`.

### NuGet / Paket

*Coming soon*

### Direct download

*Coming soon*

## IDE Integration

### Visual Studio Code

Install the official [C# extension](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csharp). OmniSharp comes bundled with this extension.

> [!WARNING]
> **Do not install C# Dev Kit!** Uninstall or disable this extension or the OmniSharp server will not start.

After you install the dll and create `omnisharp.json`, restart OmniSharp for it to take effect. Open the command palette (`Ctrl+Shift+P`) and then run `OmniSharp: Restart OmniSharp`.

### VSCodium

*Coming soon*

## How it works

This plugin runs a Bazel query on each C# file to determine which target "owns" it. A virtual project is created for each target. To inspect these queries, look at OmniSharp logs for messages from `OmniSharp.Bazel.BazelShell`.

Compiler options and project references are extracted from commandline arguments passed to the C# compiler.

To support large monorepos, this plugin is activated on-demand when a C# file is opened. It does not perform an initial scan so there will be a brief delay the first time before language services become available.

## Known issues

* Changing `deps` of a C# target is flaky. You may need to restart OmniSharp when you do this.

* If there's no BUILD file in any directory above a C# file and you create one later, the C# file may not be recognized as a part of a Bazel target unless OmniSharp restarts.

If the plugin stops working, just restart OmniSharp. Please create an issue with as much detail as possible whenever this happens.

## Contributing

Questions, bug reports, and pull requests are all welcome! Please create an issue first before you start writing code in case I'm able to provide additional context.

All contributions should comply with these requirements:

* 80 characters line width (up to 100 is acceptable if there's no good way to break it down).
* Implicit types (`var`) should not be used unless it's immediately apparent what type it is or when the type is not relevant for readability.
* Public types and methods should have xmldoc comments.
* Paket must be used as the package manager.
* MSBuild files are not allowed.

#### AI Policy

I am tentatively accepting AI-generated code as long they comply with all of the above requirements. Please clearly label any AI-generated code when you create a pull request.

This policy may be revoked at any time.

## Motivation

I love C# and I love Bazel.

But IDE support for C# with Bazel didn't exist. Until now, developers can either:

1. Maintain C# project and solution files in parallel (*which drift over time*)
2. Use Gazelle to generate Bazel targets from MSBuild files (*which makes Bazel second class*)

I dislike mixing different build systems in a repo. In my view, if you choose Bazel then you should only need BUILD files. There shouldn't be any reason to use MSBuild at all!

There are a few Roslyn-powered C# language servers out there:

* C# Dev Kit is closed-source
* DotRush doesn't have a way to extend it (that I'm aware of)
* OmniSharp has a plugin system based on MEF2 

OmniSharp was the de-facto language server for VS Code before C# Dev Kit and its extensibility makes it the preferred choice for adding Bazel integration.

---

This is a personal project done in my free time and released with permission from Google. It is not supported by Google or Bazel maintainers.
