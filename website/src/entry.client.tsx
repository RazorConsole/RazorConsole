import { hydrateRoot } from "react-dom/client";
import { StrictMode, startTransition } from "react";
import { HydratedRouter } from "react-router/dom";
import { pagePath } from "./lib/site-paths";

// Pages serves index.html aliases without a redirect; hydrate the matching directory route.
if (/\/index\.html$/i.test(window.location.pathname)) {
  const { pathname, search, hash } = window.location;
  window.history.replaceState(window.history.state, "", `${pagePath(pathname)}${search}${hash}`);
}

startTransition(() => {
  hydrateRoot(
    document,
    <StrictMode>
      <HydratedRouter />
    </StrictMode>
  );
});
