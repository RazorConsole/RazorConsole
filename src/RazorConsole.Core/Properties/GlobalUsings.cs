// Copyright (c) RazorConsole. All rights reserved.

#if NET9_0_OR_GREATER
global using Lock = System.Threading.Lock;
#else
global using Lock = System.Object;
#endif
