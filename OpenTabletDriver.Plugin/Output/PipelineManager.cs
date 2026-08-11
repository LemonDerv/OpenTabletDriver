using System;
using System.Collections.Generic;

#nullable enable

namespace OpenTabletDriver.Plugin.Output
{
    public class PipelineManager<T>
    {
        protected void Link<T2>(IPipelineElement<T>? source, T2? destination)
        {
            if (source != null && destination != null)
            {
                switch (destination)
                {
                    case IPipelineElement<T> nextElement:
                        source.Emit += nextElement.Consume;
                        break;
                    case IList<IPipelineElement<T>> nextList:
                        if (nextList.Count > 0)
                            source.Emit += nextList[0].Consume;
                        break;
                    case IEnumerable<IPipelineElement<T>> nextGroup:
                        foreach (var element in nextGroup)
                        {
                            source.Emit += element.Consume;
                            break; // only link to the first element
                        }
                        break;
                    case Action<T> nextAction:
                        source.Emit += nextAction;
                        break;
                }
            }
        }

        protected void Unlink<T2>(IPipelineElement<T>? source, T2? destination)
        {
            if (source != null && destination != null)
            {
                switch (destination)
                {
                    case IPipelineElement<T> nextElement:
                        source.Emit -= nextElement.Consume;
                        break;
                    case IList<IPipelineElement<T>> nextList:
                        if (nextList.Count > 0)
                            source.Emit -= nextList[0].Consume;
                        break;
                    case IEnumerable<IPipelineElement<T>> nextGroup:
                        foreach (var element in nextGroup)
                        {
                            source.Emit -= element.Consume;
                            break; // only unlink from the first element
                        }
                        break;
                    case Action<T> nextAction:
                        source.Emit -= nextAction;
                        break;
                }
            }
        }

        protected void LinkElements(IList<IPipelineElement<T>> elements)
        {
            if (elements != null && elements.Count > 0)
            {
                for (int i = 1; i < elements.Count; i++)
                {
                    Link(elements[i - 1], elements[i]);
                }
            }
        }

        protected void UnlinkElements(IList<IPipelineElement<T>> elements)
        {
            if (elements != null && elements.Count > 0)
            {
                for (int i = 1; i < elements.Count; i++)
                {
                    Unlink(elements[i - 1], elements[i]);
                }
            }
        }

        protected void LinkAll(params object[] elements)
        {
            for (int i = 0; i < elements.Length - 1; i++)
            {
                var prev = elements[i];
                var next = elements[i + 1];

                if (prev is IPipelineElement<T> prevElement)
                {
                    Link(prevElement, next);
                }
                else if (prev is IList<IPipelineElement<T>> prevList)
                {
                    LinkElements(prevList);
                    if (prevList.Count > 0)
                        Link(prevList[prevList.Count - 1], next);
                }
            }
        }

        protected void UnlinkAll(params object[] elements)
        {
            for (int i = 0; i < elements.Length - 1; i++)
            {
                var prev = elements[i];
                var next = elements[i + 1];

                if (prev is IPipelineElement<T> prevElement)
                {
                    Unlink(prevElement, next);
                }
                else if (prev is IList<IPipelineElement<T>> prevList)
                {
                    UnlinkElements(prevList);
                    if (prevList.Count > 0)
                        Unlink(prevList[prevList.Count - 1], next);
                }
            }
        }

        protected IList<IPositionedPipelineElement<T>> GroupElements(IList<IPositionedPipelineElement<T>> elements, PipelinePosition position)
        {
            var count = 0;
            for (var i = 0; i < elements.Count; i++)
            {
                if (elements[i].Position == position)
                    count++;
            }

            if (count == 0)
                return Array.Empty<IPositionedPipelineElement<T>>();

            var result = new IPositionedPipelineElement<T>[count];
            var index = 0;
            for (var i = 0; i < elements.Count; i++)
            {
                if (elements[i].Position == position)
                    result[index++] = elements[i];
            }

            return result;
        }
    }
}
