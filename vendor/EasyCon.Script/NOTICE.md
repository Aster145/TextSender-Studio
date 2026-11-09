# EasyCon.Script

Source: <https://github.com/EasyConNS/EasyCon>

Snapshot: `76cdb216d00fc5e0fb8510dede85390a6c5f3d40`, directory `src/EasyCon.Script`.

The source in `src/` comes from the supplied EasyCon checkout. The standalone project file in this directory was added for TextSender Studio and replaces EasyCon's solution-wide build properties. No EasyCon UI, device drivers, personal configuration, or native DLL loader is included.

TextSender Studio adds an optional `EcxExecutionPosition` observer to the desktop interpreter. It publishes an atomic function/program-counter snapshot at existing execution-budget boundaries and before host calls. It does not change bytecode semantics or insert delays; source-line lookup and UI refresh happen outside the interpreter. The emitter now invokes its existing source-line registration helper, and the linker assigns function image indices, completing the snapshot's source-location metadata.

EasyCon is distributed under the GNU General Public License v3. See [LICENSE](LICENSE). Copyright remains with the original EasyCon authors and contributors. TextSender Studio's host adapter is separate from these compiler and interpreter sources.

Function declarations, external declarations and explicit `CALL` statements also accept underscore-prefixed names. These tokens remain constants elsewhere; existing constant declarations and expressions retain their syntax. Chinese identifiers were already supported by the supplied lexer.
