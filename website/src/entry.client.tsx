import { hydrateRoot } from "react-dom/client";
import { StrictMode, startTransition } from "react";
import { HydratedRouter } from "react-router/dom";

startTransition(() => {
  hydrateRoot(
    document,
    <StrictMode>
      <HydratedRouter />
    </StrictMode>
  );
});
