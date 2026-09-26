/**
 * WASM exports from the C# runtime
 */
export interface WasmExports {
	Registry: {
		HandleTerminalInput: (elementId: string, data: string) => Promise<void>;
		UnregisterComponent: (elementId: string) => Promise<void>;
		RegisterComponent: (elementId: string, cols: number, rows: number) => Promise<void>;
		HandleKeyboardEvent: (
			componentName: string,
			xtermKey: string,
			domKey: string,
			ctrlKey: boolean,
			altKey: boolean,
			shiftKey: boolean
		) => Promise<void>;
		HandleResize: (
			componentName: string,
			cols: number,
			rows: number
		) => void;
	};
}

/**
 * Creates the .NET runtime and returns the assembly exports.
 * This is the only exported function from main.js.
 */
export declare function createRuntimeAndGetExports(): Promise<WasmExports>;
