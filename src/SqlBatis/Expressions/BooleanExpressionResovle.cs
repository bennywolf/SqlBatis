﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using SqlBatis.Attributes;

namespace SqlBatis.Expressions
{
    public class BooleanExpressionResovle : ExpressionResovle
    {
        private readonly string _prefix = "@";

        private bool _isNotExpression = false;

        private readonly Dictionary<string, object> _parameters = new Dictionary<string, object>();

        public BooleanExpressionResovle(Expression expression)
            : base(expression)
        {
            _parameters = new Dictionary<string, object>();
        }

        public BooleanExpressionResovle(Expression expression, Dictionary<string, object> parameters)
            : base(expression)
        {
            _parameters = parameters;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (IsParameterExpression(node))
            {
                SetParameterName(node);
            }
            else
            {
                SetParameterValue(node);
            }
            return node;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Operator))
            {
                if (node.Arguments.Count == 2)
                {
                    _textBuilder.Append("(");
                    SetParameterName(node.Arguments[0] as MemberExpression);
                    _textBuilder.Append($" {Operator.ResovleExpressionType(node.Method.Name)} ");
                    var value = VisitExpressionValue(node.Arguments[1]);
                    if (node.Method.Name == nameof(Operator.StartsWith) || node.Method.Name == nameof(Operator.NotStartsWith))
                    {
                        SetParameterValue(Expression.Constant($"{value}%", typeof(string)));
                    }
                    else if (node.Method.Name == nameof(Operator.EndsWith) || node.Method.Name == nameof(Operator.NotEndsWith))
                    {
                        SetParameterValue(Expression.Constant($"%{value}", typeof(string)));
                    }
                    else if (node.Method.Name == nameof(Operator.Contains) || node.Method.Name == nameof(Operator.NotContains))
                    {
                        SetParameterValue(Expression.Constant($"%{value}%", typeof(string)));
                    }
                    else
                    {
                        SetParameterValue(Expression.Constant(value));
                    }
                    _textBuilder.Append(")");
                }
            }
            else if (IsLikeExpression(node))
            {
                _textBuilder.Append("(");
                object value = null;
                if (IsParameterExpression(node.Object))
                {
                    SetParameterName(node.Object as MemberExpression);
                    value = VisitExpressionValue(node.Arguments[0]);
                }
                else
                {
                    SetParameterName(node.Arguments[0] as MemberExpression);
                    value = VisitExpressionValue(node.Object);
                }
                if (_isNotExpression)
                {
                    _isNotExpression = false;
                    _textBuilder.Append(" NOT LIKE ");
                }
                else
                {
                    _textBuilder.Append(" LIKE ");
                }
                if (node.Method.Name == nameof(string.Contains))
                {
                    SetParameterValue(Expression.Constant($"%{value}%"));
                }
                else if (node.Method.Name == nameof(string.StartsWith))
                {
                    SetParameterValue(Expression.Constant($"{value}%"));
                }
                else
                {
                    SetParameterValue(Expression.Constant($"%{value}"));
                }
                _textBuilder.Append(")");
            }
            else if (IsInExpression(node))
            {
                _textBuilder.Append("(");
                Expression arguments1 = null;
                Expression arguments2 = null;
                if (node.Arguments.Count == 1)
                {
                    arguments1 = node.Object;
                    arguments2 = node.Arguments[0];
                }
                else
                {
                    arguments1 = node.Arguments[0];
                    arguments2 = node.Arguments[1];
                }
                SetParameterName(arguments2 as MemberExpression);
                if (_isNotExpression)
                {
                    _isNotExpression = false;
                    _textBuilder.Append(" NOT IN ");
                }
                else
                {
                    _textBuilder.Append(" IN ");
                }
                SetParameterValue(arguments1 as MemberExpression);
                _textBuilder.Append(")");
            }
            else if (node.Method.DeclaringType.GetCustomAttribute(typeof(FunctionAttribute), true) != null)
            {
                var function = new FunctionExpressionResovle(node).Resovle();
                _textBuilder.Append(function);
            }
            else
            {
                SetParameterValue(node);
            }
            return node;
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            _textBuilder.Append("(");
            Visit(node.Left);
            if (node.Right is ConstantExpression right && right.Value == null && (node.NodeType == ExpressionType.Equal || node.NodeType == ExpressionType.NotEqual))
            {
                _textBuilder.AppendFormat(" {0}", node.NodeType == ExpressionType.Equal ? "IS NULL" : "IS NOT NULL");
            }
            else
            {
                _textBuilder.Append($" {Operator.ResovleExpressionType(node.NodeType)} ");
                Visit(node.Right);
            }
            _textBuilder.Append(")");
            return node;
        }

        protected override Expression VisitNew(NewExpression node)
        {
            SetParameterValue(node);
            return node;
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Not)
            {
                if (node.Operand is MethodCallExpression methodCallExpression
                    && (IsInExpression(methodCallExpression) || IsLikeExpression(methodCallExpression)))
                {
                    _isNotExpression = true;
                }
                else
                {
                    if (node.Type == typeof(int) || node.Type == typeof(int?))
                    {
                        _textBuilder.AppendFormat("{0} ", Operator.ResovleExpressionType("~"));
                    }
                    else
                    {
                        _textBuilder.AppendFormat("{0} ", Operator.ResovleExpressionType("NOT"));
                    }
                }
                Visit(node.Operand);
            }
            else
            {
                Visit(node.Operand);
            }
            return node;
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            SetParameterValue(node);
            return node;
        }

        private void SetParameterName(MemberExpression expression)
        {
            var name = GetColumnName(expression.Member.DeclaringType, expression.Member.Name);
            _textBuilder.Append(name);
        }

        private void SetParameterValue(Expression expression)
        {
            var value = VisitExpressionValue(expression);
            var parameterName = $"P_{_parameters.Count}";
            _parameters.Add(parameterName, value);
            _textBuilder.Append($"{_prefix}{parameterName}");
        }

        private bool IsLikeExpression(MethodCallExpression node)
        {
            return
                node.Arguments.Count == 1 && node.Method.DeclaringType == typeof(string)
                &&
                (
                    nameof(string.Contains).Equals(node.Method.Name)
                    || nameof(string.StartsWith).Equals(node.Method.Name)
                    || nameof(string.EndsWith).Equals(node.Method.Name)
                );
        }

        private bool IsParameterExpression(Expression expression)
        {
            return expression is MemberExpression memberExpression &&
                memberExpression.Expression?.NodeType == ExpressionType.Parameter;
        }

        private bool IsInExpression(MethodCallExpression node)
        {
            if (typeof(IEnumerable).IsAssignableFrom(node.Method.DeclaringType))
            {
                return node.Method.Name == nameof(Enumerable.Contains) && node.Arguments.Count == 1;
            }
            else
            {
                return node.Method.Name == nameof(Enumerable.Contains) && node.Arguments.Count == 2;
            }
        }
    }
}
