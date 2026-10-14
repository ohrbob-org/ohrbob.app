# Ohrbob App Branding & Styling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign the Avalonia player app's colors and theme to match the light yellow `#fff4d8`, orange `#fc5d00`, purple `#544ff4`, and dark navy `#001957` colors from the web frontend.

**Architecture:** 
- Refactor styling assets in `MainWindow.axaml` and app-wide resources.
- Switch the application from a generic dark theme to the specific light-and-brand-accent theme.

**Tech Stack:** C#, Avalonia XAML, Styles.

## Global Constraints
- Target Framework: .NET 10
- Exact colors: Orange `#fc5d00`, Purple `#544ff4`, Light Cream `#fff4d8`, Deep Navy `#001957`, White `#FFFFFF`.

---

### Task 1: Update MainWindow.axaml Styling

**Files:**
- Modify: `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Views/MainWindow.axaml`

- [ ] **Step 1: Replace generic Spotify dark colors with official Ohrbob brand colors**
Apply these color updates:
  - Onboarding Background: `#fff4d8`
  - Onboarding Card Background: `#FFFFFF`
  - Input field background: `#FFFFFF`, borders: `#001957`
  - Main button background: `#fc5d00`, text: `#FFFFFF`
  - Left Sidebar Background: `#001957` (Ohrbob Deep Navy)
  - Left Sidebar text/item colors: Active item is purple `#544ff4` instead of `#282828`. Station names and title is white.
  - Right Player Panel Background: `#fff4d8` (Cream) or `#FFFFFF` (White)
  - Track titles & station headers: `#001957` (Deep Navy)
  - Circular player controls: `#001957` background with White icons, play button background: `#fc5d00` (Orange).
  - Progress slider & timeline: Active/accent color: `#fc5d00`.

- [ ] **Step 2: Build the project and verify it compiles**
Run:
```bash
dotnet build /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Ohrbob.App.csproj
```
Expected: SUCCESS

---

### Task 2: Update Headless Tests to Reflect UI/Color Verification

**Files:**
- Modify: `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests/Views/MainWindowE2ETests.cs`

- [ ] **Step 1: Update assertions if there are any hardcoded color/theme checks in the tests**
- [ ] **Step 2: Run all tests to ensure zero failures**
Run:
```bash
dotnet test /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests/Ohrbob.App.Tests.csproj
```
Expected: PASS
