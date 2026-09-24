#!/usr/bin/env python3
"""Compile every HoloTable assembly exactly as Unity splits them, without Unity.

For each .asmdef in the package a netstandard2.1 project is generated with:
  * the .cs files Unity would put in that assembly (its folder, minus nested asmdef folders),
  * ProjectReferences only to the assemblies the .asmdef lists (so a missing asmdef reference
    fails here just like it fails in Unity),
  * UnityEngine module reference DLLs (NuGet), and UnityEditor.dll for Editor-only assemblies,
  * minimal API stubs for external packages, one stub assembly per real Unity assembly name
    (Tools/UnityCompileCheck/Stubs/<AssemblyName>/).

Usage:  python3 Tools/UnityCompileCheck/check.py
"""
import json
import os
import shutil
import subprocess
import sys
from xml.sax.saxutils import escape

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
PACKAGE = os.path.join(REPO, "Packages", "com.adrimg.holotable")
STUBS = os.path.join(HERE, "Stubs")
OUT = os.path.join(HERE, "Generated")

# Precompiled DLLs that Unity auto-references for every asmdef (overrideReferences: false).
AUTO_REFERENCED_STUBS = ["Vuforia.Unity.Engine"]

DEFINES = "HOLO_ARFOUNDATION;HOLO_ARF6;HOLO_VUFORIA;HOLO_XR_HANDS;HOLO_INPUT_SYSTEM;UNITY_2021_3_OR_NEWER"

COMMON = """  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NoWarn>$(NoWarn);CS0649;CS0414;CS0067;NU1701</NoWarn>
    <DefineConstants>$(DefineConstants);{defines}</DefineConstants>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <AssemblyName>{name}</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="UnityEngine.Modules" Version="2021.3.33" PrivateAssets="all" />
  </ItemGroup>
"""

EDITOR_REFERENCE = """  <ItemGroup>
    <PackageReference Include="Unity3D.SDK" Version="2021.1.14.1" PrivateAssets="all" ExcludeAssets="all" GeneratePathProperty="true" />
    <Reference Include="UnityEditor"><HintPath>$(PkgUnity3D_SDK)/lib/UnityEditor.dll</HintPath><Private>false</Private></Reference>
  </ItemGroup>
"""


def find_asmdefs():
    result = {}
    for dirpath, dirnames, filenames in os.walk(PACKAGE):
        dirnames[:] = [d for d in dirnames if not d.endswith("~")]
        for name in filenames:
            if name.endswith(".asmdef"):
                with open(os.path.join(dirpath, name), encoding="utf-8") as handle:
                    data = json.load(handle)
                result[data["name"]] = {"dir": dirpath, "data": data}
    return result


def sources_for(asm_dir, all_dirs):
    """.cs files under asm_dir that are not inside a nested asmdef folder."""
    nested = [d for d in all_dirs if d != asm_dir and d.startswith(asm_dir + os.sep)]
    files = []
    for dirpath, dirnames, filenames in os.walk(asm_dir):
        if any(dirpath == n or dirpath.startswith(n + os.sep) for n in nested):
            continue
        files += [os.path.join(dirpath, f) for f in filenames if f.endswith(".cs")]
    return sorted(files)


def write_project(name, sources, project_refs, editor):
    folder = os.path.join(OUT, name)
    os.makedirs(folder, exist_ok=True)
    lines = ['<Project Sdk="Microsoft.NET.Sdk">', COMMON.format(defines=DEFINES, name=name)]
    lines.append("  <ItemGroup>")
    lines += [f'    <Compile Include="{escape(s)}" />' for s in sources]
    lines.append("  </ItemGroup>")
    if project_refs:
        lines.append("  <ItemGroup>")
        lines += [f'    <ProjectReference Include="../{r}/{r}.csproj" />' for r in project_refs]
        lines.append("  </ItemGroup>")
    if editor:
        lines.append(EDITOR_REFERENCE)
    lines.append("</Project>")
    path = os.path.join(folder, name + ".csproj")
    with open(path, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")
    return path


def main() -> int:
    shutil.rmtree(OUT, ignore_errors=True)
    asmdefs = find_asmdefs()
    stubs = {d: os.path.join(STUBS, d) for d in os.listdir(STUBS) if os.path.isdir(os.path.join(STUBS, d))}

    # Stub assemblies may reference other stubs they build on (e.g. ARFoundation → ARSubsystems).
    stub_deps = {"Unity.XR.ARFoundation": ["Unity.XR.ARSubsystems"]}
    for name, folder in stubs.items():
        files = sorted(os.path.join(folder, f) for f in os.listdir(folder) if f.endswith(".cs"))
        write_project(name, files, stub_deps.get(name, []), editor=False)

    problems = []
    all_dirs = [v["dir"] for v in asmdefs.values()]
    projects = []
    for name, info in asmdefs.items():
        data = info["data"]
        refs = []
        for ref in data.get("references", []):
            if ref in asmdefs or ref in stubs:
                refs.append(ref)
            else:
                problems.append(f"{name}: reference '{ref}' has no asmdef in the package and no stub in Tools/UnityCompileCheck/Stubs")
        if not data.get("overrideReferences", False):
            refs += [s for s in AUTO_REFERENCED_STUBS if s in stubs and s not in refs]
        editor = data.get("includePlatforms") == ["Editor"]
        projects.append(write_project(name, sources_for(info["dir"], all_dirs), refs, editor))

    if problems:
        print("\n".join(problems))
        return 1

    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    failed = False
    for project in sorted(projects):
        name = os.path.splitext(os.path.basename(project))[0]
        result = subprocess.run(["dotnet", "build", project, "--nologo", "-v", "q"], capture_output=True, text=True, env=env)
        errors = sorted({line.split(" [")[0].replace(REPO + os.sep, "") for line in result.stdout.splitlines()
                         if ": error " in line or ": warning " in line})
        status = "ok" if result.returncode == 0 else "FAILED"
        print(f"{status:6} {name}")
        for line in errors:
            print(f"       {line}")
        failed |= result.returncode != 0

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
