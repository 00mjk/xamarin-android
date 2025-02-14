using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Xamarin.Android.Tasks.LLVMIR
{
	abstract partial class LlvmIrGenerator
	{
		// In code generated by clang, function attributes are determined based on the compiler optimization,
		// security arguments, architecture specific flags and so on.  For our needs we will have but a
		// handful of such sets, based on what clang generates for our native runtime.  As such, there is nothing
		// "smart" about how we select the attributes, they must match the compiler output for XA runtime, that's all.
		//
		// Sets are initialized here with the options common to all architectures, the rest is added in the architecture
		// specific derived classes.
		//
		public const int FunctionAttributesXamarinAppInit = 0;
		public const int FunctionAttributesJniMethods = 1;
		public const int FunctionAttributesCall = 2;

		protected readonly Dictionary<int, LlvmFunctionAttributeSet> FunctionAttributes = new Dictionary<int, LlvmFunctionAttributeSet> ();

		bool codeOutputInitialized = false;

		/// <summary>
		/// Writes the function definition up to the opening curly brace
		/// </summary>
		public void WriteFunctionStart (LlvmIrFunction function, string? comment = null)
		{
			if (function == null) {
				throw new ArgumentNullException (nameof (function));
			}

			LlvmFunctionAttributeSet? attributes = null;
			if (function.AttributeSetID >= 0 && !FunctionAttributes.TryGetValue (function.AttributeSetID, out attributes)) {
				throw new InvalidOperationException ($"Function '{function.Name}' refers to attribute set that does not exist (ID: {function.AttributeSetID})");
			}

			Output.WriteLine ();
			if (!String.IsNullOrEmpty (comment)) {
				foreach (string line in comment.Split ('\n')) {
					WriteCommentLine (line);
				}
			}

			if (attributes != null) {
				WriteCommentLine ($"Function attributes: {attributes.Render ()}");
			}

			Output.Write ($"define {GetKnownIRType (function.ReturnType)} @{function.Name} (");
			WriteFunctionParameters (function.Parameters, writeNames: true);
			Output.Write(") local_unnamed_addr ");
			if (attributes != null) {
				Output.Write ($"#{function.AttributeSetID.ToString (CultureInfo.InvariantCulture)}");
			}
			Output.WriteLine ();
			Output.WriteLine ("{");
		}

		void CodeRenderType (LlvmIrVariable variable, StringBuilder? builder = null)
		{
			if (variable.NativeFunction != null) {
				if (builder == null) {
					WriteFunctionSignature (variable.NativeFunction);
				} else {
					builder.Append (RenderFunctionSignature (variable.NativeFunction));
				}
				return;
			}

			string extraPointer = variable.IsNativePointer ? "*" : String.Empty;
			string irType = $"{GetKnownIRType (variable.Type)}{extraPointer}";
			if (builder == null) {
				Output.Write (irType);
			} else {
				builder.Append (irType);
			}
		}

		void WriteFunctionParameters (IList<LlvmIrFunctionParameter>? parameters, bool writeNames)
		{
			string rendered = RenderFunctionParameters (parameters, writeNames);
			if (String.IsNullOrEmpty (rendered)) {
				return;
			}

			Output.Write (rendered);
		}

		public string RenderFunctionParameters (IList<LlvmIrFunctionParameter>? parameters, bool writeNames)
		{
			if (parameters == null || parameters.Count == 0) {
				return String.Empty;
			}

			var sb = new StringBuilder ();
			bool first = true;
			foreach (LlvmIrFunctionParameter p in parameters) {
				if (!first) {
					sb.Append (", ");
				} else {
					first = false;
				}

				CodeRenderType (p, sb);

				if (writeNames) {
					sb.Append ($" %{p.Name}");
				}
			}

			return sb.ToString ();
		}

		public void WriteFunctionSignature (LlvmNativeFunctionSignature sig, bool isPointer = true)
		{
			Output.Write (RenderFunctionSignature (sig, isPointer));
		}

		public string RenderFunctionSignature (LlvmNativeFunctionSignature sig, bool isPointer = true)
		{
			if (sig == null) {
				throw new ArgumentNullException (nameof (sig));
			}

			var sb = new StringBuilder ();
			sb.Append (GetKnownIRType (sig.ReturnType));
			sb.Append (" (");
			sb.Append (RenderFunctionParameters (sig.Parameters, writeNames: false));
			sb.Append (")");
			if (isPointer) {
				sb.Append ('*');
			}

			return sb.ToString ();
		}

		/// <summary>
		/// Writes the epilogue of a function, including the return statement <b>if</b> the function return
		/// type is <c>void</c>.
		/// </summary>
		public void WriteFunctionEnd (LlvmIrFunction function)
		{
			if (function == null) {
				throw new ArgumentNullException (nameof (function));
			}

			if (function.ReturnType == typeof (void)) {
				EmitReturnInstruction (function);
			}

			Output.WriteLine ("}");
		}

		/// <summary>
		/// Emits the <c>ret</c> statement using <paramref name="retvar"/> as the returned value. If <paramref name="retvar"/>
		/// is <c>null</c>, <c>void</c> is used as the return value.
		/// </summary>
		public void EmitReturnInstruction (LlvmIrFunction function, LlvmIrFunctionLocalVariable? retVar = null)
		{
			if (function == null) {
				throw new ArgumentNullException (nameof (function));
			}

			string ret = retVar != null ? $"{GetKnownIRType(retVar.Type)} %{retVar.Name}" : "void";
			Output.WriteLine ($"{function.Indent}ret {ret}");
		}

		/// <summary>
		/// Emits the <c>store</c> instruction (https://llvm.org/docs/LangRef.html#store-instruction), which stores data from a local
		/// variable into either local or global destination. If types of <paramref name="source"/> and <paramref name="destination"/>
		/// differ, <paramref name="destination"/> is bitcast to the type of <paramref name="source"/>.  It is responsibility of the
		/// caller to make sure the two types are compatible and/or convertible to each other.
		/// <summary>
		public void EmitStoreInstruction (LlvmIrFunction function, LlvmIrFunctionLocalVariable source, LlvmIrVariableReference destination)
		{
			if (function == null) {
				throw new ArgumentNullException (nameof (function));
			}

			// TODO: implement bitcast, if necessary
			Output.Write ($"{function.Indent}store ");
			CodeRenderType (source);
			Output.Write ($" %{source.Name}, ");
			CodeRenderType (destination);
			Output.WriteLine ($"* {destination.Reference}, align {GetTypeSize (destination.Type).ToString (CultureInfo.InvariantCulture)}");
		}

		/// <summary>
		/// Emits the <c>load</c> instruction (https://llvm.org/docs/LangRef.html#load-instruction)
		/// </summary>
		public LlvmIrFunctionLocalVariable EmitLoadInstruction (LlvmIrFunction function, LlvmIrVariableReference source, string? resultVariableName = null)
		{
			if (function == null) {
				throw new ArgumentNullException (nameof (function));
			}

			var sb = new StringBuilder ();
			CodeRenderType (source, sb);

			string variableType = sb.ToString ();
			LlvmIrFunctionLocalVariable result = function.MakeLocalVariable (source, resultVariableName);
			Output.WriteLine ($"{function.Indent}%{result.Name} = load {variableType}, {variableType}* @{source.Name}, align {PointerSize.ToString (CultureInfo.InvariantCulture)}");

			return result;
		}

		/// <summary>
		/// Emits the <c>icmp</c> comparison instruction (https://llvm.org/docs/LangRef.html#icmp-instruction)
		/// </summary>
		public LlvmIrFunctionLocalVariable EmitIcmpInstruction (LlvmIrFunction function, LlvmIrIcmpCond cond, LlvmIrVariableReference variable, string expectedValue, string? resultVariableName = null)
		{
			if (function == null) {
				throw new ArgumentNullException (nameof (function));
			}

			string condOp;
			switch (cond) {
				case LlvmIrIcmpCond.Equal:  // equal
					condOp = "eq";
					break;

				case LlvmIrIcmpCond.NotEqual:  // not equal
					condOp = "ne";
					break;

				case LlvmIrIcmpCond.UnsignedGreaterThan: // unsigned greater than
					condOp = "ugt";
					break;

				case LlvmIrIcmpCond.UnsignedGreaterOrEqual: // unsigned greater or equal
					condOp = "uge";
					break;

				case LlvmIrIcmpCond.UnsignedLessThan: // unsigned less than
					condOp = "ult";
					break;

				case LlvmIrIcmpCond.UnsignedLessOrEqual: // unsigned less or equal
					condOp = "ule";
					break;

				case LlvmIrIcmpCond.SignedGreaterThan: // signed greater than,
					condOp = "sgt";
					break;

				case LlvmIrIcmpCond.SignedGreaterOrEqual: // signed greater or equal
					condOp = "sge";
					break;

				case LlvmIrIcmpCond.SignedLessThan: // signed less than
					condOp = "slt";
					break;

				case LlvmIrIcmpCond.SignedLessOrEqual: // signed less or equal
					condOp = "sle";
					break;

				default:
					throw new InvalidOperationException ($"Unsupported `icmp` conditional '{cond}'");
			}

			var sb = new StringBuilder ();
			CodeRenderType (variable, sb);

			string variableType = sb.ToString ();
			LlvmIrFunctionLocalVariable result = function.MakeLocalVariable (variable.Type, resultVariableName);

			Output.WriteLine ($"{function.Indent}%{result.Name} = icmp {condOp} {variableType} {variable.Reference}, {expectedValue}");

			return result;
		}

		public void EmitBrInstruction (LlvmIrFunction function, LlvmIrVariableReference condVariable, string labelTrue, string labelFalse)
		{
			if (function == null) {
				throw new ArgumentNullException (nameof (function));
			}

			Output.WriteLine ($"{function.Indent}br i1 {condVariable.Reference}, label %{labelTrue}, label %{labelFalse}");
		}

		public void EmitBrInstruction (LlvmIrFunction function, string label)
		{
			if (function == null) {
				throw new ArgumentNullException (nameof (function));
			}

			Output.WriteLine ($"{function.Indent}br label %{label}");
		}

		public void EmitLabel (LlvmIrFunction function, string labelName)
		{
			if (function == null) {
				throw new ArgumentNullException (nameof (function));
			}

			Output.WriteLine ($"{labelName}:");
		}

		public LlvmIrFunctionLocalVariable? EmitCall (LlvmIrFunction function, LlvmIrVariableReference targetRef, List<LlvmIrFunctionArgument>? arguments = null,
		                                              string? resultVariableName = null, LlvmIrCallMarker marker = LlvmIrCallMarker.Tail, int AttributeSetID = FunctionAttributesCall)
		{
			if (function == null) {
				throw new ArgumentNullException (nameof (function));
			}

			if (targetRef == null) {
				throw new ArgumentNullException (nameof (targetRef));
			}

			LlvmNativeFunctionSignature targetSignature = targetRef.NativeFunction;
			if (targetSignature == null) {
				throw new ArgumentException ("must be reference to native function", nameof (targetRef));
			}

			if (targetSignature.Parameters.Count > 0) {
				if (arguments == null) {
					throw new ArgumentNullException (nameof (arguments));
				}

				if (targetSignature.Parameters.Count != arguments.Count) {
					throw new ArgumentException ($"number of passed parameters ({arguments.Count}) does not match number of parameters in function signature ({targetSignature.Parameters.Count})", nameof (arguments));
				}
			}

			bool returnsValue = targetSignature.ReturnType != typeof(void);
			LlvmIrFunctionLocalVariable? result = null;

			Output.Write (function.Indent);
			if (returnsValue) {
				result = function.MakeLocalVariable (targetSignature.ReturnType, resultVariableName);
				Output.Write ($"%{result.Name} = ");
			}

			switch (marker) {
				case LlvmIrCallMarker.Tail:
					Output.Write ("tail ");
					break;

				case LlvmIrCallMarker.MustTail:
					Output.Write ("musttail ");
					break;

				case LlvmIrCallMarker.NoTail:
					Output.Write ("notail ");
					break;

				case LlvmIrCallMarker.None:
					break;

				default:
					throw new InvalidOperationException ($"Unsupported call marker '{marker}'");
			}

			Output.Write ($"call {GetKnownIRType (targetSignature.ReturnType)} {targetRef.Reference} (");

			if (targetSignature.Parameters.Count > 0) {
				for (int i = 0; i < targetSignature.Parameters.Count; i++) {
					LlvmIrFunctionParameter parameter = targetSignature.Parameters[i];
					LlvmIrFunctionArgument argument = arguments[i];

					AssertValidType (i, parameter, argument);

					if (i > 0) {
						Output.Write (", ");
					}

					string extra = parameter.IsNativePointer ? "*" : String.Empty;
					string paramType = $"{GetKnownIRType (parameter.Type)}{extra}";
					Output.Write ($"{paramType} ");

					if (argument.Value is LlvmIrFunctionLocalVariable variable) {
						Output.Write ($"%{variable.Name}");
					} else if (parameter.Type.IsNativePointer () || parameter.IsNativePointer) {
						if (parameter.IsCplusPlusReference) {
							Output.Write ("nonnull ");
						}

						string ptrSize = PointerSize.ToString (CultureInfo.InvariantCulture);
						Output.Write ($"align {ptrSize} dereferenceable({ptrSize}) ");

						if (argument.Value is LlvmIrVariableReference variableRef) {
							bool needBitcast = parameter.Type != argument.Type;

							if (needBitcast) {
								Output.Write ("bitcast (");
								CodeRenderType (variableRef);
								Output.Write ("* ");
							}

							Output.Write (variableRef.Reference);

							if (needBitcast) {
								Output.Write ($" to {paramType})");
							}
						} else {
							throw new InvalidOperationException ($"Unexpected pointer type in argument {i}, '{argument.Type}'");
						}
					} else {
						Output.Write (argument.Value.ToString ());
					}
				}
			}

			Output.Write (")");

			if (AttributeSetID >= 0) {
				if (!FunctionAttributes.ContainsKey (AttributeSetID)) {
					throw new InvalidOperationException ($"Unknown attribute set ID {AttributeSetID}");
				}
				Output.Write ($" #{AttributeSetID.ToString (CultureInfo.InvariantCulture)}");
			}
			Output.WriteLine ();

			return result;

			static void AssertValidType (int index, LlvmIrFunctionParameter parameter, LlvmIrFunctionArgument argument)
			{
				if (argument.Type == typeof(LlvmIrFunctionLocalVariable) || argument.Type == typeof(LlvmIrVariableReference)) {
					return;
				}

				if (parameter.Type != typeof(IntPtr)) {
					if (argument.Type != parameter.Type) {
						ThrowException ();
					}
					return;
				}

				if (argument.Type.IsNativePointer ()) {
					return;
				}

				if (typeof(LlvmIrVariable).IsAssignableFrom (argument.Type) &&
				    argument.Value is LlvmIrVariable variable &&
				    (variable.IsNativePointer || variable.NativeFunction != null)) {
					return;
				}

				ThrowException ();

				void ThrowException ()
				{
					throw new InvalidOperationException ($"Argument {index} type '{argument.Type}' does not match the expected function parameter type '{parameter.Type}'");
				}
			}
		}

		/// <summary>
		/// Emits the <c>phi</c> instruction (https://llvm.org/docs/LangRef.html#phi-instruction) for a function pointer type
		/// </summary>
		public LlvmIrFunctionLocalVariable EmitPhiInstruction (LlvmIrFunction function, LlvmIrVariableReference target, List<(LlvmIrVariableReference variableRef, string label)> pairs, string? resultVariableName = null)
		{
			if (function == null) {
				throw new ArgumentNullException (nameof (function));
			}

			LlvmIrFunctionLocalVariable result = function.MakeLocalVariable (target, resultVariableName);
			Output.Write ($"{function.Indent}%{result.Name} = phi ");
			CodeRenderType (target);

			bool first = true;
			foreach ((LlvmIrVariableReference variableRef, string label) in pairs) {
				if (first) {
					first = false;
					Output.Write (' ');
				} else {
					Output.Write (", ");
				}

				Output.Write ($"[{variableRef.Reference}, %{label}]");
			}
			Output.WriteLine ();

			return result;
		}

		public void InitCodeOutput ()
		{
			if (codeOutputInitialized) {
				return;
			}

			InitFunctionAttributes ();
			InitCodeMetadata ();
			codeOutputInitialized = true;
		}

		protected virtual void InitCodeMetadata ()
		{
			MetadataManager.Add ("llvm.linker.options");
		}

		protected virtual void InitFunctionAttributes ()
		{
			FunctionAttributes[FunctionAttributesXamarinAppInit] = new LlvmFunctionAttributeSet {
				new MinLegalVectorWidthFunctionAttribute (0),
				new MustprogressFunctionAttribute (),
				new NofreeFunctionAttribute (),
				new NorecurseFunctionAttribute (),
				new NosyncFunctionAttribute (),
				new NoTrappingMathFunctionAttribute (true),
				new NounwindFunctionAttribute (),
				new SspstrongFunctionAttribute (),
				new StackProtectorBufferSizeFunctionAttribute (8),
				new UwtableFunctionAttribute (),
				new WillreturnFunctionAttribute (),
				new WriteonlyFunctionAttribute (),
			};

			FunctionAttributes[FunctionAttributesJniMethods] = new LlvmFunctionAttributeSet {
				new MinLegalVectorWidthFunctionAttribute (0),
				new MustprogressFunctionAttribute (),
				new NoTrappingMathFunctionAttribute (true),
				new NounwindFunctionAttribute (),
				new SspstrongFunctionAttribute (),
				new StackProtectorBufferSizeFunctionAttribute (8),
				new UwtableFunctionAttribute (),
			};

			FunctionAttributes[FunctionAttributesCall] = new LlvmFunctionAttributeSet {
				new NounwindFunctionAttribute (),
			};
		}

		void WriteAttributeSets ()
		{
			if (!codeOutputInitialized) {
				return;
			}

			WriteSet (FunctionAttributesXamarinAppInit, Output);
			WriteSet (FunctionAttributesJniMethods, Output);
			WriteSet (FunctionAttributesCall, Output);

			Output.WriteLine ();

			void WriteSet (int id, TextWriter output)
			{
				output.Write ($"attributes #{id.ToString (CultureInfo.InvariantCulture)} = {{ ");
				foreach (LLVMFunctionAttribute attr in FunctionAttributes[id]) {
					output.Write (attr.Render ());
					output.Write (' ');
				}
				output.WriteLine ("}");
			}
		}
	}
}
