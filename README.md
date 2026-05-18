# VRChat Unfriend Manager (VRC:UFM)

> Copyright © 2025 [hollynt]
> This work is free. You can redistribute it and/or modify it under the
> terms of the Do What The Fuck You Want To Public License, Version 2,
> as published by Sam Hocevar. See the [LICENSE](#-license) section for more details.

**VRC:UFM** is a powerful, terminal-based utility designed for managing your VRChat friends list with speed and precision. It allows you to bulk unfriend inactive users, re-add friends from backups, and organize your list without the limitations of the standard in-game or website UI.

![S1](https://gitlab.com/hollyntii/VRChat-Unfriend-Manager/-/raw/master/Product%20Images/VRCUFMProduct.png?ref_type=heads)

## 🚀 Features

*   **Smart Inactivity Filtering:** Filter your friends list by last login time. Easily find users who haven't logged in for a specific number of Days, Months, or Years.
*   **Advanced Sorting:** Sort your list by **Last Seen (Oldest First)**, **Last Seen (Newest First)**, or Alphabetically (A-Z / Z-A).
*   **Favorites Protection:** The "Exclude Favorites" option ensures you don't accidentally remove close friends or people in your favorite groups.
*   **Bulk Unfriend & Re-Add:**
    *   **Unfriend:** Select multiple users and remove them in one go.
    *   **Re-Add:** Mistake? Restore friends by loading a previously saved JSON backup; the tool will automatically send friend requests to everyone in the file.
*   **Configuration Saving:** The app remembers your Username, Password (securely encoded), and your preferred settings (Sort Order, Inactive Filters, etc.) so you don't have to set them up every time.
*   **Safety First:**
    *   **2FA Support:** Full support for Authenticator Apps (TOTP), Email OTP, and Backup codes.
    *   **Rate-Limit Protection:** Operations include randomized delays (5-10 seconds) to keep your account safe from API spam detection.
    *   **Pause/Resume:** Need to stop? Pause the operation at any time and resume when ready.
*   **JSON Backups:** One-click backup of your currently displayed list to a timestamped `.json` file.

## 📋 Requirements

*   **[.NET 6.0 Runtime (or newer)](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)**
*   Windows 10/11 (Recommended for Toast Notifications)

## 🛠️ How to Use

### 1. Login & Setup
1.  Launch the application.
2.  Enter your VRChat credentials.
3.  Check **"Remember me"** to save your login and your UI settings (Sort order, filters, etc.) for next time.
4.  If you have 2FA enabled, enter your code when prompted.

### 2. Filtering & Sorting
*   **Exclude Favorites:** Checked by default. Uncheck this if you really want to see/remove favorited friends.
*   **Only show inactive ≥:** Check this to filter users based on how long they have been offline.
    *   *Example:* Set to `1` and `Years` to see only people who haven't logged in for over a year.
*   **Sort by:** Use the dropdown to organize the list. "Last Seen: Oldest" is the default, placing users who have **never** logged in at the very top.

### 3. Managing the List
*   **Navigation:** Use `Arrow Keys` to move up/down.
*   **Selection:** Press `Spacebar` to check/uncheck a specific user.
*   **Bulk Select:** Use the `Mark All` or `Unmark All` buttons to select everyone currently visible in the list.

### 4. Actions
*   **Unfriend Marked:** Removes all selected users. You will be asked to confirm before the process starts.
*   **Backup Displayed:** Saves the currently visible list to a JSON file (e.g., `VRChatFriends_2025-01-01.json`).
*   **Re-add from JSON:** Select a backup file to automatically send friend requests to everyone in that list.

---

## ⚠️ Disclaimer

> **USE AT YOUR OWN RISK.**
>
> *   **Unofficial Tool:** VRC:UFM is a third-party tool and is not affiliated with VRChat Inc.
> *   **Permanent Actions:** Unfriending is permanent. While the "Re-add" feature exists, it relies on the user accepting your new friend request. **Always create a Backup before running bulk operations.**
> *   **TOS:** Automating actions on your account technically falls into a grey area of VRChat's Terms of Service. This tool uses human-like delays to minimize risk, but the developer is not responsible for any administrative actions taken against your account.

---

## 🏗️ Building from Source

1.  Clone this repository.
2.  Ensure you have the .NET 6.0 SDK installed.
3.  Navigate to the project folder in your terminal.
4.  Run:
    ```bash
    dotnet run
    ```

---

[![License: WTFPL](https://img.shields.io/badge/License-WTFPL-brightgreen.svg)](http://www.wtfpl.net/)