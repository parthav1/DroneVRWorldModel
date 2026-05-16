declare const _default: {
    content: string[];
    theme: {
        extend: {
            colors: {
                void: string;
                ink: string;
                panel: string;
                cyan: string;
                lime: string;
                ember: string;
                magenta: string;
            };
            boxShadow: {
                glow: string;
                ember: string;
            };
            animation: {
                float: string;
                pulseGlow: string;
                drift: string;
                scan: string;
                reveal: string;
            };
            keyframes: {
                float: {
                    "0%, 100%": {
                        transform: string;
                    };
                    "50%": {
                        transform: string;
                    };
                };
                pulseGlow: {
                    "0%, 100%": {
                        opacity: string;
                        transform: string;
                    };
                    "50%": {
                        opacity: string;
                        transform: string;
                    };
                };
                drift: {
                    "0%": {
                        transform: string;
                    };
                    "100%": {
                        transform: string;
                    };
                };
                scan: {
                    "0%, 100%": {
                        transform: string;
                        opacity: string;
                    };
                    "50%": {
                        transform: string;
                        opacity: string;
                    };
                };
                reveal: {
                    "0%": {
                        opacity: string;
                        transform: string;
                    };
                    "100%": {
                        opacity: string;
                        transform: string;
                    };
                };
            };
        };
    };
    plugins: any[];
};
export default _default;
