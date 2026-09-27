interface Branch {
  name: string;
  commit: string;
  path: string;
}

interface Manifest {
  branches: Branch[];
  repository: string;
}

const base_url = new URL("./", window.location.href);
const storage_key = `pages_branch:${base_url.pathname}`;
const branch_select = requiredElement<HTMLSelectElement>("#branch");
const commit_link = requiredElement<HTMLAnchorElement>("#commit");
const game_frame = requiredElement<HTMLIFrameElement>("#game");
const error_message = requiredElement<HTMLParagraphElement>("#error");

function requiredElement<T extends Element>(selector: string): T {
  const element = document.querySelector<T>(selector);
  if (!element) {
    throw new Error(`Missing version selector element: ${selector}.`);
  }
  return element;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function parseManifest(value: unknown): Manifest {
  if (!isRecord(value) || typeof value.repository !== "string"
    || !/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(value.repository)
    || !Array.isArray(value.branches) || value.branches.length === 0) {
    throw new Error("Invalid branch manifest.");
  }

  const branch_names = new Set<string>();
  const branch_paths = new Set<string>();
  const branches = value.branches.map((entry: unknown): Branch => {
    if (!isRecord(entry) || typeof entry.name !== "string"
      || entry.name.length === 0 || entry.name !== entry.name.trim()
      || typeof entry.commit !== "string" || !/^[a-f0-9]{7,64}$/i.test(entry.commit)
      || typeof entry.path !== "string" || !/^(?:[A-Za-z0-9_.-]+\/)+$/.test(entry.path)
      || entry.path.split("/").some((segment) => segment === "." || segment === "..")
      || branch_names.has(entry.name) || branch_paths.has(entry.path)) {
      throw new Error("Invalid branch entry.");
    }
    branch_names.add(entry.name);
    branch_paths.add(entry.path);
    return { name: entry.name, commit: entry.commit, path: entry.path };
  });

  return { branches, repository: value.repository };
}

function readStoredBranch(): string | null {
  try {
    return window.localStorage.getItem(storage_key);
  } catch {
    return null;
  }
}

function storeBranch(name: string): void {
  try {
    window.localStorage.setItem(storage_key, name);
  } catch {
    // Version selection still works when browser storage is unavailable.
  }
}

async function initialize(): Promise<void> {
  const response = await fetch(new URL("branches.json", base_url), { cache: "no-store" });
  if (!response.ok) {
    throw new Error(`Branch manifest request failed: ${response.status}.`);
  }
  const manifest = parseManifest(await response.json() as unknown);
  const branches = new Map(manifest.branches.map((branch) => [branch.name, branch]));
  const default_branch = branches.get("main") ?? manifest.branches[0]!;
  let active_branch: string | null = null;

  function branchFromUrl(allow_storage: boolean): Branch {
    const requested = new URL(window.location.href).searchParams.get("branch");
    const preferred = requested ?? (allow_storage ? readStoredBranch() : null);
    return (preferred ? branches.get(preferred) : undefined) ?? default_branch;
  }

  function selectBranch(branch: Branch, history_mode: "push" | "replace"): void {
    const url = new URL(window.location.href);
    url.searchParams.set("branch", branch.name);
    if (url.href !== window.location.href) {
      if (history_mode === "push") {
        window.history.pushState(null, "", url);
      } else {
        window.history.replaceState(null, "", url);
      }
    }

    branch_select.value = branch.name;
    storeBranch(branch.name);
    commit_link.textContent = branch.commit.slice(0, 7);
    commit_link.href = `https://github.com/${manifest.repository}/commit/${branch.commit}`;
    commit_link.setAttribute("aria-label", `Commit ${branch.commit.slice(0, 7)} on ${branch.name}`);
    commit_link.hidden = false;
    game_frame.title = `Chroma Drop — ${branch.name}`;
    game_frame.hidden = false;
    if (active_branch !== branch.name) {
      active_branch = branch.name;
      game_frame.src = new URL(branch.path, base_url).href;
    }
  }

  for (const branch of manifest.branches) {
    branch_select.add(new Option(branch.name, branch.name));
  }
  branch_select.disabled = false;
  branch_select.addEventListener("change", () => {
    const selected = branches.get(branch_select.value);
    if (selected) {
      selectBranch(selected, "push");
    }
  });
  window.addEventListener("popstate", () => selectBranch(branchFromUrl(false), "replace"));
  game_frame.addEventListener("load", () => {
    game_frame.focus();
    game_frame.contentWindow?.focus();
  });
  selectBranch(branchFromUrl(true), "replace");
}

void initialize().catch((error: unknown) => {
  branch_select.disabled = true;
  game_frame.hidden = true;
  commit_link.hidden = true;
  error_message.textContent = "Versions are unavailable.";
  error_message.hidden = false;
  console.error(error);
});
